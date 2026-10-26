using System;
using System.IO;
using System.Text;

namespace HotelSynth
{
    public static class WavIO
    {
        public static void Write(string path, float[] left, float[] right = null, int sampleRate = Dsp.SR)
        {
            int ch = right == null ? 1 : 2;
            int n = left.Length;
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = n * ch * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)ch);
                w.Write(sampleRate);
                w.Write(sampleRate * ch * 2);
                w.Write((short)(ch * 2));
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                for (int i = 0; i < n; i++)
                {
                    w.Write(ToShort(left[i]));
                    if (right != null) w.Write(ToShort(i < right.Length ? right[i] : 0f));
                }
            }
        }

        static short ToShort(float v)
        {
            if (float.IsNaN(v)) v = 0;
            int s = (int)Math.Round(Math.Max(-1f, Math.Min(1f, v)) * 32767f);
            return (short)s;
        }
    }
}
