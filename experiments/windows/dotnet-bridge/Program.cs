using System.Runtime.InteropServices;
using System.Text.Json;

internal static unsafe class Program
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Main(string[] args)
    {
        Check(args.Length == 1, "Pass an absolute native library path");
        Check(nuint.Size == 8, "This probe targets 64-bit processes");
        var library = NativeLibrary.Load(Path.GetFullPath(args[0]));
        // Keep the module loaded for the process lifetime, including all native output releases.
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly,
            (name, _, _) => name == "compositor_native" ? library : 0);
        Pixels();
        Selection();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "passed", boundary = "C# P/Invoke", platform = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
            pointerBytes = nint.Size, sizeTBytes = nuint.Size, fixedSignedOutputBytes = sizeof(long),
            sourceAlgorithmsCalled = 8, nativeEntryPointsCalled = 17, traceAllocationReleaseCycles = 1000
        }));
    }
    private static void Pixels()
    {
        byte[] rgba = [32,64,96,128, 0xCD,0xCD,0xCD,0xCD, 32,64,96,128, 0xCD,0xCD,0xCD,0xCD];
        var original = (byte[])rgba.Clone();
        byte* alpha = stackalloc byte[4] {0,0xCD,0,0xCD};
        nuint* bounds = stackalloc nuint[4];
        fixed (byte* pixels = rgba)
        {
            // A managed buffer remains pinned throughout synchronous native use, even across a GC.
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Native.brush_alpha_bounds(pixels,1,2,8,bounds);
            Check(bounds[0]==0 && bounds[1]==0 && bounds[2]==1 && bounds[3]==2,"RGBA bounds/stride");
            Native.layer_extract_alpha(pixels,8,alpha,2,1,2);
            Check(alpha[0]==128 && alpha[2]==128 && alpha[1]==0xCD && alpha[3]==0xCD,"gray padding");
            Native.layer_unpremultiply_opaque(pixels,8,1,2);
            Native.layer_restore_alpha(pixels,8,alpha,2,1,2);
            Check(rgba.SequenceEqual(original),"alpha round trip");
            byte* destination = stackalloc byte[16];
            new Span<byte>(destination,16).Fill(0xCD);
            Native.lens_distort(pixels,destination,1,2,8,0);
            Check(new ReadOnlySpan<byte>(destination,16).SequenceEqual(original),"identity lens and padding");
            float* tables = stackalloc float[768];
            for (int i=0;i<768;i++) tables[i]=(i%256)/255f;
            Native.levels_apply(pixels,1,tables); // Packed count; skip row padding explicitly.
            Native.levels_apply(pixels+8,1,tables);
            Check(rgba.SequenceEqual(original),"identity levels");
            double* bins = stackalloc double[1024];
            new Span<double>(bins,1024).Clear();
            Native.levels_histogram(pixels,null,1,bins);
            Check(Math.Abs(bins[256+64]-128.0/255)<1e-12,"histogram alpha weight");
            byte* table = stackalloc byte[768];
            for (int i=0;i<768;i++) table[i]=(byte)(i%3==0?255:0);
            Native.adjust_gradient_map(pixels,1,2,8,table);
            Check(pixels[0]==128 && pixels[1]==0 && pixels[8]==128 && pixels[9]==0,"gradient RGBA/stride");
            Native.adjust_grain(pixels,1,2,8,70,2,45,42,10,20,0.5);
            Native.noise_add(pixels,1,2,8,20,1,0,42);
            for (int row=0;row<2;row++)
            {
                Check(pixels[row*8+3]==128,"noise/grain preserve alpha");
                for (int c=0;c<3;c++) Check(pixels[row*8+c]<=128,"premultiplied range");
                for (int c=4;c<8;c++) Check(pixels[row*8+c]==0xCD,"RGBA padding preserved");
            }
            pixels[0]=255;
            Native.rgba_clamp_premultiplied(pixels,1);
            Check(pixels[0]==128,"clamp packed pixel");
            byte* coverage = stackalloc byte[2] {0,0};
            var before = (byte[])rgba.Clone();
            Check(Native.content_fill(pixels,8,coverage,1,1,2)==1,"fill no selection status");
            Check(Native.spot_heal(pixels,coverage,1,2,8,1,0,42)==0,"heal no selection status");
            Check(rgba.SequenceEqual(before),"empty coverage leaves pixels unchanged");
        }
    }
    private static void Selection()
    {
        byte* gray = stackalloc byte[8] {0,0,255,255, 0,255,255,255};
        long* bounds = stackalloc long[4] {-1,-1,-1,-1};
        Native.compositor_heal_bounds(gray,2,2,4,bounds);
        Check(bounds[0]==1 && bounds[1]==1 && bounds[2]==2 && bounds[3]==2,"fixed-width signed bounds");
        byte* rgba = stackalloc byte[12] {255,0,0,255, 0,0,0,255, 255,0,0,255};
        byte* mask = stackalloc byte[3];
        Check(Native.compositor_wand_mask(rgba,3,1,12,0,0,0,0,0,mask)==2,"fixed-width selected count");
        Check(mask[0]==255 && mask[1]==0 && mask[2]==255,"packed mask");
        for (int i=0;i<1000;i++)
        {
            int status = Native.wand_trace(mask,3,1,out var points,out var count,out var loops,out var loopCount);
            try
            {
                Check(status==0 && count==8 && loopCount==2,"native trace counts");
                Check(Marshal.ReadInt32(loops)==4 && Marshal.ReadInt32(loops,4)==4,"native int32 loop data");
                for (int p=0;p<(int)count;p++)
                {
                    int x=Marshal.ReadInt32(points,p*8), y=Marshal.ReadInt32(points,p*8+4);
                    Check(x>=0 && x<=3 && y>=0 && y<=1,"native point data");
                }
            }
            finally { Native.compositor_free(points); Native.compositor_free(loops); }
        }
        Native.compositor_free(0);
    }
}
