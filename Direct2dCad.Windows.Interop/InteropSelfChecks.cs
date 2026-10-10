using System.Runtime.CompilerServices;
using Windows.Win32.System.Com;

[assembly:InternalsVisibleTo("Direct2dCad.Avalonia")]

namespace Direct2dCad.Windows.Interop;

internal static unsafe class InteropSelfChecks
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0")]
    internal static void VerifyPrintTicket(string printer, nint devMode, int copies)
    {
        var ticket = VectorPrinting.CreatePrintTicket(printer, devMode);
        if (ticket is null) throw new InvalidOperationException("Print settings did not produce a PrintTicket.");
        try
        {
            STATSTG stat = default;
            ticket->Stat(&stat, 1); // STATFLAG_NONAME
            if (stat.cbSize == 0 || stat.cbSize > 4 * 1024 * 1024)
                throw new InvalidDataException("Unexpected PrintTicket stream length.");
            var bytes = new byte[checked((int)stat.cbSize)];
            uint read = 0;
            fixed (byte* buffer = bytes) ticket->Read(buffer, (uint)bytes.Length, &read);
            if (read != bytes.Length) throw new EndOfStreamException("Incomplete PrintTicket stream.");
            using var input = new MemoryStream(bytes, false);
            var xml = System.Xml.Linq.XDocument.Load(input);
            System.Xml.Linq.XNamespace framework = "http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework";
            if (xml.Root?.Name != framework + "PrintTicket")
                throw new InvalidDataException("Driver conversion did not return PrintTicket XML.");
            var copyParameter = xml.Descendants(framework + "ParameterInit")
                .SingleOrDefault(e => ((string?)e.Attribute("name"))?.Split(':')[^1] == "JobCopiesAllDocuments");
            if (copyParameter?.Element(framework + "Value")?.Value != copies.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidDataException("PrintTicket lost the requested copy count.");
        }
        finally { ticket->Release(); }
    }

    internal static void VerifyOleCallbacks()
    {
        using var session=OleSession.CreateFixture();
        var notifications=new List<bool>();session.Changed=notifications.Add;
        var site=session.CallbackSite;site->AddRef();
        var sink=NativeComWrappers.Query<IAdviseSink>((IUnknown*)site);
        try
        {
            var siteIdentity=NativeComWrappers.Query<IUnknown>((IUnknown*)site);
            var sinkIdentity=NativeComWrappers.Query<IUnknown>((IUnknown*)sink);
            try{if(siteIdentity!=sinkIdentity)throw new InvalidOperationException("OLE callback interfaces do not share COM identity.");}
            finally{siteIdentity->Release();sinkIdentity->Release();}
            sink->OnDataChange(null,null);sink->OnViewChange(1,-1);sink->OnSave();sink->OnClose();site->SaveObject();
            if(!notifications.SequenceEqual(new[]{false,false,true,true,true}))throw new InvalidOperationException("Native COM callback dispatch lost update or save notifications.");
            session.Changed=_=>throw new InvalidOperationException("Callback boundary fixture");
            try{site->SaveObject();throw new InvalidDataException("A failing managed callback returned success.");}
            catch(Exception ex)when(ex.HResult==unchecked((int)0x80131509)){ }
            session.Dispose();sink->OnSave();sink->OnViewChange(1,-1);
            if(notifications.Count!=5)throw new InvalidOperationException("Disposed OLE sessions received a late callback.");
        }
        finally{sink->Release();site->Release();}
    }
}
