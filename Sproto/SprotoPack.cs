#nullable disable
using System.IO;

namespace Sproto
{
    public class SprotoPack
    {
        private readonly MemoryStream buffer = new();
        private readonly byte[] tmp = new byte[8];

        private void WriteFF(byte[] src, int offset, long pos, int n)
        {
            int align8 = (n + 7) & (~7);
            long curPos = buffer.Position;

            buffer.Seek(pos, SeekOrigin.Begin);
            buffer.WriteByte(0xff);
            buffer.WriteByte((byte)(align8 / 8 - 1));
            buffer.Write(src, offset, n);
            for (int i = 0; i < align8 - n; i++)
                buffer.WriteByte(0);
            buffer.Seek(curPos, SeekOrigin.Begin);
        }

        private int PackSeg(byte[] src, long offset, int ffN)
        {
            byte header = 0;
            int notzero = 0;
            long headerPos = buffer.Position;
            buffer.Seek(1, SeekOrigin.Current);

            for (int i = 0; i < 8; i++)
            {
                if (src[offset + i] != 0)
                {
                    notzero++;
                    header |= (byte)(1 << i);
                    buffer.WriteByte(src[offset + i]);
                }
            }

            if ((notzero == 7 || notzero == 6) && ffN > 0)
                notzero = 8;
            if (notzero == 8)
            {
                buffer.Seek(headerPos, SeekOrigin.Begin);
                return ffN > 0 ? 8 : 10;
            }

            buffer.Seek(headerPos, SeekOrigin.Begin);
            buffer.WriteByte(header);
            buffer.Seek(headerPos, SeekOrigin.Begin);
            return notzero + 1;
        }

        public byte[] Pack(byte[] data, int len = 0)
        {
            Clear();

            int srcSz = len == 0 ? data.Length : len;
            byte[] ffSrc = null;
            int ffSrcStart = 0;
            long ffDstStart = 0;
            int ffN = 0;

            byte[] src = data;
            int offset = 0;

            for (int i = 0; i < srcSz; i += 8)
            {
                offset = i;
                int padding = i + 8 - srcSz;
                if (padding > 0)
                {
                    for (int j = 0; j < 8 - padding; j++)
                        tmp[j] = src[i + j];
                    for (int j = 0; j < padding; j++)
                        tmp[7 - j] = 0;
                    src = tmp;
                    offset = 0;
                }

                int n = PackSeg(src, offset, ffN);
                if (n == 10)
                {
                    ffSrc = src;
                    ffSrcStart = offset;
                    ffDstStart = buffer.Position;
                    ffN = 1;
                }
                else if (n == 8 && ffN > 0)
                {
                    ++ffN;
                    if (ffN == 256)
                    {
                        WriteFF(ffSrc, ffSrcStart, ffDstStart, 256 * 8);
                        ffN = 0;
                    }
                }
                else
                {
                    if (ffN > 0)
                    {
                        WriteFF(ffSrc, ffSrcStart, ffDstStart, ffN * 8);
                        ffN = 0;
                    }
                }
                buffer.Seek(n, SeekOrigin.Current);
            }

            if (ffN == 1)
                WriteFF(ffSrc, ffSrcStart, ffDstStart, 8);
            else if (ffN > 1)
                WriteFF(ffSrc, ffSrcStart, ffDstStart,
                    (ffSrc == data ? srcSz : ffSrc.Length) - ffSrcStart);

            byte[] result = new byte[buffer.Position];
            buffer.Seek(0, SeekOrigin.Begin);
            buffer.Read(result, 0, result.Length);
            return result;
        }

        public byte[] Unpack(byte[] data, int len = 0)
        {
            Clear();

            len = len == 0 ? data.Length : len;
            int srcSz = len;

            while (srcSz > 0)
            {
                byte header = data[len - srcSz];
                --srcSz;

                if (header == 0xff)
                {
                    if (srcSz < 0)
                        SprotoTypeSize.error("invalid unpack stream.");
                    int n = (data[len - srcSz] + 1) * 8;
                    if (srcSz < n + 1)
                        SprotoTypeSize.error("invalid unpack stream.");
                    buffer.Write(data, len - srcSz + 1, n);
                    srcSz -= n + 1;
                }
                else
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if ((header >> i & 1) == 1)
                        {
                            if (srcSz < 0)
                                SprotoTypeSize.error("invalid unpack stream.");
                            buffer.WriteByte(data[len - srcSz]);
                            --srcSz;
                        }
                        else
                        {
                            buffer.WriteByte(0);
                        }
                    }
                }
            }

            byte[] result = new byte[buffer.Position];
            buffer.Seek(0, SeekOrigin.Begin);
            buffer.Read(result, 0, result.Length);
            return result;
        }

        private void Clear()
        {
            buffer.Seek(0, SeekOrigin.Begin);
            System.Array.Clear(tmp, 0, tmp.Length);
        }
    }
}
