#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sproto
{
    public class SprotoTypeSerialize
    {
        private int headerIdx;
        private int headerSz;
        private int headerCap = SprotoTypeSize.sizeof_header;
        private SprotoStream data;
        private int dataIdx;
        private int lastTag = -1;
        private int index;

        public SprotoTypeSerialize(int maxFieldCount)
        {
            headerSz = SprotoTypeSize.sizeof_header + maxFieldCount * SprotoTypeSize.sizeof_field;
        }

        private void SetHeaderFn(int fn)
        {
            data[headerIdx - 2] = (byte)(fn & 0xff);
            data[headerIdx - 1] = (byte)((fn >> 8) & 0xff);
        }

        private void WriteHeaderRecord(int record)
        {
            data[headerIdx + headerCap - 2] = (byte)(record & 0xff);
            data[headerIdx + headerCap - 1] = (byte)((record >> 8) & 0xff);
            headerCap += 2;
            index++;
        }

        private void WriteUint32ToUint64Sign(bool isNegative)
        {
            byte v = (byte)(isNegative ? 0xff : 0);
            data.WriteByte(v);
            data.WriteByte(v);
            data.WriteByte(v);
            data.WriteByte(v);
        }

        private void WriteTag(int tag, int value)
        {
            int stag = tag - lastTag - 1;
            if (stag > 0)
            {
                stag = (stag - 1) * 2 + 1;
                if (stag > 0xffff)
                    SprotoTypeSize.error("tag is too big.");
                WriteHeaderRecord(stag);
            }
            WriteHeaderRecord(value);
            lastTag = tag;
        }

        private void WriteUint32(UInt32 v)
        {
            data.WriteByte((byte)(v & 0xff));
            data.WriteByte((byte)((v >> 8) & 0xff));
            data.WriteByte((byte)((v >> 16) & 0xff));
            data.WriteByte((byte)((v >> 24) & 0xff));
        }

        private void WriteUint64(UInt64 v)
        {
            data.WriteByte((byte)(v & 0xff));
            data.WriteByte((byte)((v >> 8) & 0xff));
            data.WriteByte((byte)((v >> 16) & 0xff));
            data.WriteByte((byte)((v >> 24) & 0xff));
            data.WriteByte((byte)((v >> 32) & 0xff));
            data.WriteByte((byte)((v >> 40) & 0xff));
            data.WriteByte((byte)((v >> 48) & 0xff));
            data.WriteByte((byte)((v >> 56) & 0xff));
        }

        private void FillSize(int sz)
        {
            if (sz < 0)
                SprotoTypeSize.error("fill invaild size.");
            WriteUint32((UInt32)sz);
        }

        private int EncodeInteger(UInt32 v)
        {
            FillSize(sizeof(UInt32));
            WriteUint32(v);
            return SprotoTypeSize.sizeof_length + sizeof(UInt32);
        }

        private int EncodeUint64(UInt64 v)
        {
            FillSize(sizeof(UInt64));
            WriteUint64(v);
            return SprotoTypeSize.sizeof_length + sizeof(UInt64);
        }

        private int EncodeBytes(byte[] s)
        {
            FillSize(s.Length);
            data.Write(s, 0, s.Length);
            return SprotoTypeSize.sizeof_length + s.Length;
        }

        private int EncodeString(string str)
        {
            byte[] s = Encoding.UTF8.GetBytes(str);
            return EncodeBytes(s);
        }

        private int EncodeStruct(SprotoTypeBase obj)
        {
            int szPos = data.Position;
            data.Seek(SprotoTypeSize.sizeof_length, SeekOrigin.Current);
            int len = obj.encode(data);
            int curPos = data.Position;
            data.Seek(szPos, SeekOrigin.Begin);
            FillSize(len);
            data.Seek(curPos, SeekOrigin.Begin);
            return SprotoTypeSize.sizeof_length + len;
        }

        private void Clear()
        {
            index = 0;
            headerIdx = 2;
            lastTag = -1;
            data = null;
            headerCap = SprotoTypeSize.sizeof_header;
        }

        public void write_decimal(double d, double floor, int tag)
        {
            Int64 v = (Int64)(d * floor + 0.5);
            write_integer(v, tag);
        }

        public void write_decimal(List<double> dList, double floor, int tag)
        {
            if (dList == null || dList.Count <= 0) return;
            var integerList = new List<Int64>();
            foreach (double v in dList)
                integerList.Add((Int64)(v * floor + 0.5));
            write_integer(integerList, tag);
        }

        public void write_double(double v, int tag)
        {
            UnionValue u = new UnionValue();
            u.real_v = v;
            EncodeUint64(u.integer_v);
            WriteTag(tag, 0);
        }

        public void write_double(List<double> doubleList, int tag)
        {
            if (doubleList == null || doubleList.Count <= 0) return;
            int size = doubleList.Count * 8;
            FillSize(size + 1);
            data.WriteByte(8);
            UnionValue u = new UnionValue();
            foreach (double v in doubleList)
            {
                u.real_v = v;
                WriteUint64(u.integer_v);
            }
            WriteTag(tag, 0);
        }

        public void write_integer(Int64 integer, int tag)
        {
            Int64 vh = integer >> 31;
            int sz = (vh == 0 || vh == -1) ? sizeof(UInt32) : sizeof(UInt64);
            int value = 0;

            if (sz == sizeof(UInt32))
            {
                UInt32 v = (UInt32)integer;
                if (v < 0x7fff)
                {
                    value = (int)((v + 1) * 2);
                    sz = 2;
                }
                else
                {
                    sz = EncodeInteger(v);
                }
            }
            else if (sz == sizeof(UInt64))
            {
                UInt64 v = (UInt64)integer;
                sz = EncodeUint64(v);
            }
            else
            {
                SprotoTypeSize.error("invaild integer size.");
            }
            WriteTag(tag, value);
        }

        public void write_integer(List<Int64> integerList, int tag)
        {
            if (integerList == null || integerList.Count <= 0) return;

            int szPos = data.Position;
            data.Seek(szPos + SprotoTypeSize.sizeof_length, SeekOrigin.Begin);
            int beginPos = data.Position;
            int intLen = sizeof(UInt32);
            data.Seek(beginPos + 1, SeekOrigin.Begin);

            for (int idx = 0; idx < integerList.Count; idx++)
            {
                Int64 v = integerList[idx];
                Int64 vh = v >> 31;
                int sz = (vh == 0 || vh == -1) ? sizeof(UInt32) : sizeof(UInt64);

                if (sz == sizeof(UInt32))
                {
                    WriteUint32((UInt32)v);
                    if (intLen == sizeof(UInt64))
                        WriteUint32ToUint64Sign((v & 0x80000000) == 0 ? false : true);
                }
                else if (sz == sizeof(UInt64))
                {
                    if (intLen == sizeof(UInt32))
                    {
                        data.Seek(beginPos + 1, SeekOrigin.Begin);
                        for (int i = 0; i < idx; i++)
                            WriteUint64((UInt64)integerList[i]);
                        intLen = sizeof(UInt64);
                    }
                    WriteUint64((UInt64)v);
                }
                else
                {
                    SprotoTypeSize.error("invalid integer size(" + sz + ")");
                }
            }

            int curPos = data.Position;
            data.Seek(beginPos, SeekOrigin.Begin);
            data.WriteByte((byte)intLen);
            int size = curPos - beginPos;
            data.Seek(szPos, SeekOrigin.Begin);
            FillSize(size);
            data.Seek(curPos, SeekOrigin.Begin);
            WriteTag(tag, 0);
        }

        public void write_boolean(bool b, int tag)
        {
            write_integer(b ? 1 : 0, tag);
        }

        public void write_boolean(List<bool> bList, int tag)
        {
            if (bList == null || bList.Count <= 0) return;
            FillSize(bList.Count);
            foreach (bool v in bList)
                data.WriteByte((byte)(v ? 1 : 0));
            WriteTag(tag, 0);
        }

        public void write_binary(byte[] bytes, int tag)
        {
            EncodeBytes(bytes);
            WriteTag(tag, 0);
        }

        public void write_binary(List<byte[]> bytesList, int tag)
        {
            if (bytesList == null || bytesList.Count <= 0) return;
            int sz = 0;
            foreach (byte[] v in bytesList)
                sz += SprotoTypeSize.sizeof_length + v.Length;
            FillSize(sz);
            foreach (byte[] v in bytesList)
                EncodeBytes(v);
            WriteTag(tag, 0);
        }

        public void write_string(string str, int tag)
        {
            EncodeString(str);
            WriteTag(tag, 0);
        }

        public void write_string(List<string> strList, int tag)
        {
            if (strList == null || strList.Count <= 0) return;
            int sz = 0;
            foreach (string v in strList)
                sz += SprotoTypeSize.sizeof_length + Encoding.UTF8.GetByteCount(v);
            FillSize(sz);
            foreach (string v in strList)
                EncodeString(v);
            WriteTag(tag, 0);
        }

        public void write_obj(SprotoTypeBase obj, int tag)
        {
            EncodeStruct(obj);
            WriteTag(tag, 0);
        }

        public void write_obj<T>(List<T> objList, int tag) where T : SprotoTypeBase
        {
            if (objList == null || objList.Count <= 0) return;
            int szPos = data.Position;
            data.Seek(SprotoTypeSize.sizeof_length, SeekOrigin.Current);
            foreach (SprotoTypeBase v in objList)
                EncodeStruct(v);
            int curPos = data.Position;
            int sz = curPos - szPos - SprotoTypeSize.sizeof_length;
            data.Seek(szPos, SeekOrigin.Begin);
            FillSize(sz);
            data.Seek(curPos, SeekOrigin.Begin);
            WriteTag(tag, 0);
        }

        public void write_obj<TK, TV>(Dictionary<TK, TV> map, int tag) where TV : SprotoTypeBase
        {
            if (map == null || map.Count <= 0) return;
            int szPos = data.Position;
            data.Seek(SprotoTypeSize.sizeof_length, SeekOrigin.Current);
            foreach (var pair in map)
                EncodeStruct(pair.Value);
            int curPos = data.Position;
            int sz = curPos - szPos - SprotoTypeSize.sizeof_length;
            data.Seek(szPos, SeekOrigin.Begin);
            FillSize(sz);
            data.Seek(curPos, SeekOrigin.Begin);
            WriteTag(tag, 0);
        }

        public void open(SprotoStream stream)
        {
            Clear();
            data = stream;
            headerIdx = stream.Position + headerCap;
            dataIdx = data.Seek(headerSz, SeekOrigin.Current);
        }

        public int close()
        {
            SetHeaderFn(index);
            int upCount = headerSz - headerCap;
            data.MoveUp(dataIdx, upCount);
            int count = data.Position - headerIdx + SprotoTypeSize.sizeof_header;
            Clear();
            return count;
        }
    }
}
