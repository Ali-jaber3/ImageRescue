using System.Text;

namespace ImageRescue.App.Recovery;

// Conservative contiguous-file carving. Container boundaries are not a decode guarantee.
public static class AdditionalFormats
{
    public const long MaxFileBytes = 512L * 1024 * 1024;
    public sealed record Match(long End, string Extension);
    public static Match? Detect(CachedReader r, long s, ReadOnlySpan<byte> header, ScanOptions o)
    {
        if (header.Length < 12) return null;
        long limit = s + Math.Min(MaxFileBytes, r.Length - s);
        int b = header[0];
        if (o.Pdf && b == '%' && At(r,s,"%PDF-")) return Pdf(r,s,limit);
        if ((o.Office || o.Archives) && b == 'P' && U32(r,s)==0x04034b50) return Zip(r,s,limit,o);
        if (o.Office && b==0xD0 && At(r,s,"\xD0\xCF\x11\xE0\xA1\xB1\x1A\xE1")) return Compound(r,s,limit);
        if ((o.Video || o.Audio) && At(r,s+4,"ftyp")) return Mp4(r,s,limit,o);
        if (o.Audio && (At(r,s,"ID3") || b==255)) return Mp3(r,s,limit);
        if ((o.Audio || o.Video || o.OtherImages) && b=='R' && At(r,s,"RIFF"))
        {
            long end=s+8+U32(r,s+4);
            if (end>s+12 && end<=limit)
            {
                string type=Text(r,s+8,4);
                string? ext=type switch {"WAVE" when o.Audio=>"wav", "AVI " when o.Video=>"avi", "WEBP" when o.OtherImages=>"webp", _=>null};
                if(ext is not null && Riff(r,s+12,end,type)) return new(end,ext);
            }
        }
        if(o.OtherImages && b=='B' && At(r,s,"BM"))
        {
            long size=U32(r,s+2), pixels=U32(r,s+10), dib=U32(r,s+14);
            if(size>=54 && s+size<=limit && dib is 40 or 108 or 124 && pixels>=14+dib && pixels<size && U16(r,s+26)==1)
                return new(s+size,"bmp");
        }
        if(o.OtherImages && b=='G' && (At(r,s,"GIF87a") || At(r,s,"GIF89a"))) return Gif(r,s,limit);
        if((o.OtherImages || o.Raw) && ((b=='I' && At(r,s,"II\x2A\0")) || (b=='M' && At(r,s,"MM\0\x2A"))))
            return Tiff(r,s,limit,o);
        return null;
    }
    private static Match? Pdf(CachedReader r,long s,long limit)
    {
        if(r.Byte(s+5) is <'1' or >'2' || r.Byte(s+6)!='.') return null;
        // A complete first revision; incremental updates after this EOF are not merged.
        for(long p=s+8;p+5<=limit;p++)
        {
            if((p&4095)==0) r.CheckCancellation();
            if(r.Byte(p)!='%' || !At(r,p,"%%EOF")) continue;
            long from=Math.Max(s,p-128); string tail=Text(r,from,(int)(p-from));
            int x=tail.LastIndexOf("startxref",StringComparison.Ordinal);
            if(x<0 || !long.TryParse(tail[(x+9)..].Trim(),out long relative) || relative<8 || relative>=p-s) continue;
            long xp=s+relative;
            bool crossref=At(r,xp,"xref") || (r.Byte(xp)>='0' && r.Byte(xp)<='9');
            if(!crossref) continue;
            long end=p+5;
            if(r.Byte(end)==13) end++;
            if(r.Byte(end)==10) end++;
            return new(end,"pdf");
        }
        return null;
    }
    private static Match? Zip(CachedReader r,long s,long limit,ScanOptions o)
    {
        // EOCD is accepted only when every central entry points to a matching local header.
        for(long p=s+30;p+22<=limit;p++)
        {
            if((p&4095)==0) r.CheckCancellation();
            if(r.Byte(p)!=0x50 || U32(r,p)!=0x06054b50) continue;
            int entries=U16(r,p+10);
            long cdSize=U32(r,p+12), cdOffset=U32(r,p+16), end=p+22+U16(r,p+20);
            if(U16(r,p+4)!=0 || U16(r,p+6)!=0 || entries==0 || entries>10000 || U16(r,p+8)!=entries || end>limit || cdOffset+cdSize!=p-s) continue;
            long q=s+cdOffset; bool ok=true, content=false; string? ext=null;
            for(int i=0;i<entries;i++)
            {
                r.CheckCancellation();
                if(q+46>p || U32(r,q)!=0x02014b50) {ok=false;break;}
                int n=U16(r,q+28),extra=U16(r,q+30),comment=U16(r,q+32);
                long local=s+U32(r,q+42), compressed=U32(r,q+20);
                if(n==0 || q+46+n+extra+comment>p || local<s || local+30>s+cdOffset || U32(r,local)!=0x04034b50 || U16(r,q+34)!=0 || compressed==uint.MaxValue) {ok=false;break;}
                int ln=U16(r,local+26),le=U16(r,local+28);
                if(ln!=n || local+30+ln+le+compressed>s+cdOffset || Text(r,local+30,ln)!=Text(r,q+46,n)) {ok=false;break;}
                string name=Text(r,q+46,n);
                if(name=="[Content_Types].xml") content=true;
                if(name=="word/document.xml") ext="docx";
                else if(name=="xl/workbook.xml") ext="xlsx";
                else if(name=="ppt/presentation.xml") ext="pptx";
                q+=46+n+extra+comment;
            }
            if(ok && q==p)
            {
                if(content && ext is not null && o.Office) return new(end,ext);
                if(o.Archives) return new(end,"zip");
                return null;
            }
        }
        return null;
    }
    private static Match? Compound(CachedReader r,long s,long limit)
    {
        int shift=U16(r,s+30),version=U16(r,s+26);
        if(U16(r,s+28)!=0xFFFE || !((version==3 && shift==9)||(version==4 && shift==12)) || U32(r,s+72)!=0) return null;
        int sector=1<<shift; uint fats=U32(r,s+44);
        if(fats is 0 or >109 || s+sector>limit) return null;
        var fat=new List<uint>(); long maxSector=-1;
        for(int i=0;i<fats;i++)
        {
            uint id=U32(r,s+76+4*i);long p=s+(id+1L)*sector;
            if(p<s || p+sector>limit) return null;
            maxSector=Math.Max(maxSector,id);
            for(int j=0;j<sector;j+=4) fat.Add(U32(r,p+j));
        }
        for(int i=0;i<fat.Count;i++) if(fat[i]!=0xFFFFFFFF)
        {
            if(fat[i]<0xFFFFFFFA && fat[i]>=fat.Count) return null;
            maxSector=Math.Max(maxSector,i);
        }
        long end=s+(maxSector+2)*sector;
        if(end>limit) return null;
        uint dir=U32(r,s+48);var seen=new HashSet<uint>(); string? ext=null;
        while(dir!=0xFFFFFFFE)
        {
            r.CheckCancellation();
            if(dir>=fat.Count || !seen.Add(dir)) return null;
            long p=s+(dir+1L)*sector;
            if(p+sector>end) return null;
            for(int j=0;j<sector;j+=128)
            {
                int n=U16(r,p+j+64);
                if(n<2 || n>64 || (n&1)!=0 || r.Byte(p+j+66)!=2) continue;
                var bytes=new byte[n-2];r.Copy(p+j,bytes,bytes.Length);string name=Encoding.Unicode.GetString(bytes);
                if(name=="WordDocument") ext="doc";
                else if(name is "Workbook" or "Book") ext="xls";
                else if(name=="PowerPoint Document") ext="ppt";
            }
            dir=fat[(int)dir];
        }
        return ext is null?null:new(end,ext);
    }
    private static Match? Mp4(CachedReader r,long s,long limit,ScanOptions o)
    {
        long p=s;bool movie=false,data=false;string brand="";
        for(int boxes=0;boxes<100000 && p+8<=limit;boxes++)
        {
            r.CheckCancellation();
            string type=Text(r,p+4,4);long size=r.UInt32(p),header=8;
            if(type=="ftyp" && p!=s) break;
            if(type is not ("ftyp" or "moov" or "mdat" or "free" or "skip" or "wide" or "moof" or "mfra" or "sidx" or "styp" or "pdin" or "uuid")) break;
            if(size==1) { ulong wide=((ulong)r.UInt32(p+8)<<32)|r.UInt32(p+12);if(wide>long.MaxValue) return null;size=(long)wide;header=16; }
            // A zero-size box consumes a filesystem file's EOF, unknowable in a disk image.
            if(size<header || size>limit-p) return null;
            if(p==s) {if(type!="ftyp" || size<header+8) return null;brand=Text(r,p+header,4);}
            if(type=="moov") movie=true;
            if(type=="mdat" && size>header) data=true;
            p+=size;
        }
        if(!movie || !data) return null;
        // Do not turn unrelated ISO-BMFF containers (e.g. Canon CR3) into fake MP4 files.
        bool audio = brand is "M4A " or "M4B ";
        bool video = brand is "isom" or "iso2" or "iso4" or "iso5" or "iso6" or "mp41" or "mp42"
            or "avc1" or "M4V " or "M4VH" or "M4VP" or "qt  " or "MSNV" or "dash";
        if(!audio && !video) return null;
        string ext=brand=="M4B "?"m4b":audio?"m4a":brand=="qt  "?"mov":"mp4";
        if(audio ? !o.Audio : !o.Video) return null;
        return new(p,ext);
    }
    private static Match? Mp3(CachedReader r,long s,long limit)
    {
        long p=s;
        if(At(r,p,"ID3"))
        {
            int version=r.Byte(p+3);if(version is <2 or >4 || r.Byte(p+4)==255) return null;
            int size=0;for(int i=6;i<10;i++){int b=r.Byte(p+i);if(b is <0 or >127) return null;size=(size<<7)|b;}
            p+=10L+size+((version==4 && (r.Byte(s+5)&16)!=0)?10:0);
        }
        int frames=0,format=-1;
        while(p+4<=limit)
        {
            if((frames&255)==0) r.CheckCancellation();
            uint h=r.UInt32(p);int version=(int)(h>>19)&3,layer=(int)(h>>17)&3,bi=(int)(h>>12)&15,sr=(int)(h>>10)&3;
            if((h&0xFFE00000)!=0xFFE00000 || version==1 || layer!=1 || bi is 0 or 15 || sr==3) break;
            int current=version*4+sr;if(format!=-1 && format!=current) break;format=current;
            int[] rates=version==3?[0,32,40,48,56,64,80,96,112,128,160,192,224,256,320]:[0,8,16,24,32,40,48,56,64,80,96,112,128,144,160];
            int sample=(new[]{44100,48000,32000})[sr]/(version==3?1:version==2?2:4);
            int length=(version==3?144000:72000)*rates[bi]/sample+(int)((h>>9)&1);
            if(length<4 || p+length>limit) return null;
            p+=length;frames++;
        }
        if(frames<3) return null;
        if(p+128<=limit && At(r,p,"TAG")) p+=128;
        return new(p,"mp3");
    }
    private static bool Riff(CachedReader r,long p,long end,string type)
    {
        bool first=false,second=false;
        while(p+8<=end)
        {
            r.CheckCancellation();string id=Text(r,p,4);long n=U32(r,p+4),next=p+8+n+(n&1);
            if(next>end) return false;
            first|=type switch {"WAVE"=>id=="fmt " && n>=16,"AVI "=>id=="LIST" && n>=4 && At(r,p+8,"hdrl"),_=>id is "VP8 " or "VP8L" or "VP8X"};
            second|=type switch {"WAVE"=>id=="data" && n>0,"AVI "=>id=="LIST" && n>=4 && At(r,p+8,"movi"),_=>id is "VP8 " or "VP8L" or "ANMF"};
            p=next;
        }
        return p==end && first && second;
    }
    private static Match? Gif(CachedReader r,long s,long limit)
    {
        if(U16(r,s+6)==0 || U16(r,s+8)==0) return null;
        int packed=r.Byte(s+10);long p=s+13+((packed&128)!=0?3L*(1<<((packed&7)+1)):0);bool image=false;
        while(p<limit)
        {
            r.CheckCancellation();int kind=r.Byte(p++);
            if(kind==0x3B) return image?new(p,"gif"):null;
            if(kind==0x21) {p++;}
            else if(kind==0x2C)
            {
                if(p+9>limit) return null;int local=r.Byte(p+8);p+=9;
                if((local&128)!=0) p+=3L*(1<<((local&7)+1));
                if(r.Byte(p) is <2 or >8) return null;p++;image=true;
            }
            else return null;
            while(p<limit) {int n=r.Byte(p++);if(n==0) break;if(n<0 || n>limit-p)return null;p+=n;}
        }
        return null;
    }
    private static Match? Tiff(CachedReader r,long s,long limit,ScanOptions o)
    {
        bool little=r.Byte(s)=='I';
        uint V(long p,int n){uint v=0;for(int i=0;i<n;i++){int b=r.Byte(p+(little?n-1-i:i));if(b<0)return uint.MaxValue;v=(v<<8)|(uint)b;}return v;}
        long end=s+8;bool dng=false,cr2=At(r,s+8,"CR\x02\0"),pixels=false;
        var queue=new Queue<uint>();queue.Enqueue(V(s+4,4));if(cr2)queue.Enqueue(V(s+12,4));var visited=new HashSet<uint>();
        int total=0;
        while(queue.Count>0)
        {
            r.CheckCancellation();uint offset=queue.Dequeue();if(offset==0)continue;
            if(!visited.Add(offset) || ++total>64)return null;
            long p=s+offset;if(p<s+8 || p+2>limit)return null;int count=(int)V(p,2);
            if(count>4096 || p+2+12L*count+4>limit)return null;
            end=Math.Max(end,p+2+12L*count+4);var strips=new List<uint>();var lengths=new List<uint>();
            for(int i=0;i<count;i++)
            {
                long e=p+2+12L*i;int tag=(int)V(e,2),type=(int)V(e+2,2);uint n=V(e+4,4);
                int width=type switch {1 or 2 or 6 or 7=>1,3 or 8=>2,4 or 9 or 11 or 13=>4,5 or 10 or 12=>8,_=>0};
                if(width==0 || n>16_000_000)return null;
                long bytes=(long)n*width,data=bytes<=4?e+8:s+V(e+8,4);
                if(data<s || bytes>limit-data)return null;end=Math.Max(end,data+bytes);
                if(tag==50706)dng=true;
                bool pointer=tag is 330 or 34665 or 34853 or 40965;
                bool off=tag is 273 or 324 or 513,len=tag is 279 or 325 or 514;
                if(pointer || off || len)
                {
                    if(type is not (3 or 4 or 13) || n>65536)return null;
                    for(int j=0;j<n;j++){uint value=V(data+(long)j*width,width);if(pointer)queue.Enqueue(value);else if(off)strips.Add(value);else lengths.Add(value);}
                }
            }
            if(strips.Count!=lengths.Count)return null;
            for(int i=0;i<strips.Count;i++) {long finish=s+strips[i]+lengths[i];if(strips[i]<8 || lengths[i]==0 || finish>limit)return null;end=Math.Max(end,finish);pixels=true;}
            queue.Enqueue(V(p+2+12L*count,4));
        }
        if(!pixels)return null;
        string ext=dng?"dng":cr2?"cr2":"tif";
        if(ext=="tif"?!o.OtherImages:!o.Raw)return null;
        return new(end,ext);
    }
    private static bool At(CachedReader r,long p,string text)
    {
        if(p<0 || p>r.Length-text.Length)return false;
        for(int i=0;i<text.Length;i++)if(r.Byte(p+i)!=(byte)text[i])return false;
        return true;
    }
    private static string Text(CachedReader r,long p,int n)
    {
        if(n<0 || n>65535 || p<0 || p>r.Length-n)return "";
        byte[] b=new byte[n];r.Copy(p,b,n);return Encoding.Latin1.GetString(b);
    }
    private static int U16(CachedReader r,long p)=>r.Byte(p)|(r.Byte(p+1)<<8);
    private static uint U32(CachedReader r,long p)=>(uint)(U16(r,p)|(U16(r,p+2)<<16));
}
