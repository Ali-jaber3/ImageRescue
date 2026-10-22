using ImageRescue.App.Recovery;
using System.Security.Cryptography;

internal static class AdditionalFormatTests
{
    public static void Run(string work, Action<bool,string> check)
    {
        var all = new ScanOptions(false,false,true,true,true,true,true,true,true,true);
        string fixtures=Path.Combine(AppContext.BaseDirectory,"Fixtures","formats");
        var paths=Directory.GetFiles(fixtures,"sample.*").Order().ToList();
        check(paths.Count==20,"Twenty additional format fixtures are delivered");
        int number=0;
        foreach(string path in paths)
        {
            byte[] bytes=File.ReadAllBytes(path);string extension=Path.GetExtension(path)[1..];
            // Unaligned start crosses a block boundary: neither fake zero padding nor EOF bounds it.
            int offset=1024*1024-5;byte[] container=new byte[offset+bytes.Length+73];bytes.CopyTo(container,offset);
            string folder=Path.Combine(work,"format-"+extension);
            using var stream=new MemoryStream(container);
            var report=Carver.Scan(stream,"fixture",folder,null,null,default,options:all);
            check(report.Files.Count==1,"Exactly one contiguous "+extension+" candidate");
            var file=report.Files.Single();
            check(file.Offset==offset && file.Bytes==bytes.Length && file.FileType==extension.ToUpperInvariant(),"Exact boundaries and extension: "+extension);
            byte[] saved=File.ReadAllBytes(Path.Combine(folder,file.FileName));
            check(saved.SequenceEqual(bytes) && file.Sha256==Convert.ToHexString(SHA256.HashData(bytes)),"Unmodified bytes and hash: "+extension);
            using var headerStream=new MemoryStream(bytes);
            var reader=new CachedReader(headerStream,default);
            var truncated=bytes[..Math.Min(12,bytes.Length)];
            using var shortStream=new MemoryStream(truncated);
            check(AdditionalFormats.Detect(new CachedReader(shortStream,default),0,truncated,all) is null,"Truncated header rejected: "+extension);
            using var filteredStream=new MemoryStream(container);
            var filtered=Carver.Scan(filteredStream,"filtered",Path.Combine(work,"filter-extra-"+number++),null,null,default,
                options:new ScanOptions(true,false,false,Pdf: extension!="pdf"));
            check(filtered.Files.All(f=>f.FileType!=extension.ToUpperInvariant()),"Disabled format excluded: "+extension);
        }
        // Candidate start is aligned in quick mode; all matches share the same engine as deep mode.
        byte[] pdf=File.ReadAllBytes(Path.Combine(fixtures,"sample.pdf"));
        byte[] aligned=new byte[512+pdf.Length+32];pdf.CopyTo(aligned,512);
        using(var stream=new MemoryStream(aligned))
        {
            var report=Carver.Scan(stream,"quick",Path.Combine(work,"quick-pdf"),null,null,default,ScanMode.Quick,options:all);
            check(report.Files.Count==1 && report.Files[0].Offset==512,"Quick mode supports additional formats");
        }
        // Office-only must classify a ZIP container as DOCX; archive-only preserves it as ZIP.
        byte[] docx=File.ReadAllBytes(Path.Combine(fixtures,"sample.docx"));
        foreach(bool office in new[]{true,false})
        {
            using var stream=new MemoryStream(docx);
            var report=Carver.Scan(stream,"office-zip",Path.Combine(work,"office-zip-"+office),null,null,default,
                options:new ScanOptions(false,false,false,Office:office,Archives:!office));
            check(report.Files.Count==1 && report.Files[0].FileType==(office?"DOCX":"ZIP") && !report.Files[0].IsSaved,
                "Office and archive policies retain container classification: "+office);
        }
        byte[] bad=File.ReadAllBytes(Path.Combine(fixtures,"sample.zip"));
        bad[^6]=0xff;bad[^5]=0xff; // corrupt central directory offset
        using(var stream=new MemoryStream(bad))
            check(Carver.Scan(stream,"bad-zip",Path.Combine(work,"bad-extra-zip"),null,null,default,options:all).Files.Count==0,
                "ZIP with invalid central directory rejected");
        byte[] hugeBox=[0xff,0xff,0xff,0xff,102,116,121,112,105,115,111,109,0,0,0,0];
        using(var stream=new MemoryStream(hugeBox))
            check(Carver.Scan(stream,"bad-box",Path.Combine(work,"bad-extra-box"),null,null,default,options:all).Files.Count==0,
                "Out-of-bounds media box rejected");
        foreach(byte[] seed in new[]{"%PDF-1.7"u8.ToArray(),"PK\x03\x04"u8.ToArray(),"RIFF"u8.ToArray(),"II*\0"u8.ToArray(),"GIF89a"u8.ToArray()})
        {
            var random=new Random(42);byte[] data=new byte[4096];random.NextBytes(data);seed.CopyTo(data,0);
            using var stream=new MemoryStream(data);
            check(AdditionalFormats.Detect(new CachedReader(stream,default),0,data.AsSpan(0,16),all) is null,"Random signature alone is not a file: "+Convert.ToHexString(seed));
        }
    }
}
