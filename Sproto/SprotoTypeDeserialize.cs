#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace Sproto
{
    public class SprotoTypeDeserialize
    {
        private SprotoTypeReader reader;
        private int beginDataPos;
        private int curFieldPos;
        private int fn;
        private int tag = -1;
        private int value;

        public SprotoTypeDeserialize() { }

        public SprotoTypeDeserialize(byte[] data)
        {
            init(data);
        }

        public SprotoTypeDeserialize(SprotoTypeReader reader)
        {
            init(reader);
        }

        public void init(byte[] data, int offset = 0)
        {
            Clear();
            reader = new SprotoTypeReader(data, offset, data.Length);
            InitReader();
        }

        public void init(SprotoTypeReader reader)
        {
            Clear();
            this.reader = reader;
            InitReader();
        }

        private void InitReader()
        {
            fn = ReadWord();
            int headerLength = SprotoTypeSize.sizeof_header + fn * SprotoTypeSize.sizeof_field;
            beginDataPos = headerLength;
            curFieldPos = reader.Position;

            if (reader.Length < headerLength)
                SprotoTypeSize.error("invalid decode header.");

            reader.Seek(beginDataPos);
        }

        private static UInt64 Expand64(UInt32 v)
        {
            UInt64 value = v;
            if ((value & 0x80000000) != 0)
                value |= 0xffffffff00000000;
            return value;
        }

        private int ReadWord()
            => reader.ReadByte() | (reader.ReadByte() << 8);

        private UInt32 ReadDword()
            => (UInt32)reader.ReadByte()
             | ((UInt32)reader.ReadByte() << 8)
             | ((UInt32)reader.ReadByte() << 16)
             | ((UInt32)reader.ReadByte() << 24);

        private UInt32 ReadArraySize()
        {
            if (value >= 0)
                SprotoTypeSize.error("invalid array value.");
            UInt32 sz = ReadDword();
            return sz;
        }

        private UInt64 ReadUint64()
        {
            UInt32 low = ReadDword();
            UInt32 hi = ReadDword();
            return (UInt64)low | (UInt64)hi << 32;
        }

        public int read_tag()
        {
            int pos = reader.Position;
            reader.Seek(curFieldPos);

            while (reader.Position < beginDataPos)
            {
                tag++;
                int val = ReadWord();

                if ((val & 1) == 0)
                {
                    curFieldPos = reader.Position;
                    reader.Seek(pos);
                    value = val / 2 - 1;
                    return tag;
                }
                tag += val / 2;
            }

            reader.Seek(pos);
            return -1;
        }

        public double read_decimal(double floor)
            => read_integer() / floor;

        public List<double> read_decimal_list(double floor)
        {
            var l = read_integer_list();
            if (l == null) return null;
            var ret = new List<double>();
            foreach (Int64 v in l)
                ret.Add(v / floor);
            return ret;
        }

        public double read_double()
        {
            UInt32 sz = ReadDword();
            if (sz == 8)
            {
                UnionValue v = new UnionValue();
                v.integer_v = ReadUint64();
                return v.real_v;
            }
            SprotoTypeSize.error("read invalid double size (" + sz + ")");
            return 0.0;
        }

        public List<double> read_double_list()
        {
            UInt32 sz = ReadArraySize();
            if (sz == 0) return new List<double>();
            int len = reader.ReadByte();
            sz--;

            if (len != 8)
                SprotoTypeSize.error("invalid intlen (" + len + ")");

            var list = new List<double>();
            UnionValue u = new UnionValue();
            for (int i = 0; i < sz / 8; i++)
            {
                u.integer_v = ReadUint64();
                list.Add(u.real_v);
            }
            return list;
        }

        public Int64 read_integer()
        {
            if (value >= 0)
                return value;

            UInt32 sz = ReadDword();
            if (sz == sizeof(UInt32))
                return (Int64)Expand64(ReadDword());
            if (sz == sizeof(UInt64))
                return (Int64)ReadUint64();

            SprotoTypeSize.error("read invalid integer size (" + sz + ")");
            return 0;
        }

        public List<Int64> read_integer_list()
        {
            UInt32 sz = ReadArraySize();
            if (sz == 0) return new List<Int64>();

            int len = reader.ReadByte();
            sz--;

            if (len == sizeof(UInt32))
            {
                if (sz % sizeof(UInt32) != 0)
                    SprotoTypeSize.error("error array size(" + sz + ")@sizeof(Uint32)");
                var list = new List<Int64>();
                for (int i = 0; i < sz / sizeof(UInt32); i++)
                    list.Add((Int64)Expand64(ReadDword()));
                return list;
            }

            if (len == sizeof(UInt64))
            {
                if (sz % sizeof(UInt64) != 0)
                    SprotoTypeSize.error("error array size(" + sz + ")@sizeof(Uint64)");
                var list = new List<Int64>();
                for (int i = 0; i < sz / sizeof(UInt64); i++)
                    list.Add((Int64)ReadUint64());
                return list;
            }

            SprotoTypeSize.error("error intlen(" + len + ")");
            return null;
        }

        public bool read_boolean()
        {
            if (value < 0)
                SprotoTypeSize.error("read invalid boolean.");
            return value != 0;
        }

        public List<bool> read_boolean_list()
        {
            UInt32 sz = ReadArraySize();
            var list = new List<bool>();
            for (int i = 0; i < sz; i++)
                list.Add(reader.ReadByte() != 0);
            return list;
        }

        public byte[] read_binary()
        {
            UInt32 sz = ReadDword();
            byte[] buffer = new byte[sz];
            reader.Read(buffer, 0, buffer.Length);
            return buffer;
        }

        public List<byte[]> read_binary_list()
        {
            UInt32 sz = ReadArraySize();
            var list = new List<byte[]>();
            while (sz > 0)
            {
                if (sz < SprotoTypeSize.sizeof_length)
                    SprotoTypeSize.error("error array size.");
                UInt32 hsz = ReadDword();
                sz -= (UInt32)SprotoTypeSize.sizeof_length;
                if (hsz > sz)
                    SprotoTypeSize.error("error array object.");
                byte[] buffer = new byte[hsz];
                reader.Read(buffer, 0, buffer.Length);
                list.Add(buffer);
                sz -= hsz;
            }
            return list;
        }

        public string read_string()
        {
            byte[] buffer = read_binary();
            return Encoding.UTF8.GetString(buffer);
        }

        public List<string> read_string_list()
        {
            UInt32 sz = ReadArraySize();
            var list = new List<string>();
            while (sz > 0)
            {
                if (sz < SprotoTypeSize.sizeof_length)
                    SprotoTypeSize.error("error array size.");
                UInt32 hsz = ReadDword();
                sz -= (UInt32)SprotoTypeSize.sizeof_length;
                if (hsz > sz)
                    SprotoTypeSize.error("error array object.");
                byte[] buffer = new byte[hsz];
                reader.Read(buffer, 0, buffer.Length);
                list.Add(Encoding.UTF8.GetString(buffer));
                sz -= hsz;
            }
            return list;
        }

        public T read_obj<T>() where T : SprotoTypeBase, new()
        {
            int sz = (int)ReadDword();
            var r = new SprotoTypeReader(reader.Buffer, reader.Offset, sz);
            reader.Seek(reader.Position + sz);
            T obj = new T();
            obj.Init(r);
            return obj;
        }

        private T ReadElement<T>(SprotoTypeReader r, UInt32 remaining, out UInt32 readSize) where T : SprotoTypeBase, new()
        {
            readSize = 0;
            if (remaining < SprotoTypeSize.sizeof_length)
                SprotoTypeSize.error("error array size.");
            UInt32 hsz = ReadDword();
            remaining -= (UInt32)SprotoTypeSize.sizeof_length;
            readSize += (UInt32)SprotoTypeSize.sizeof_length;
            if (hsz > remaining)
                SprotoTypeSize.error("error array object.");
            r.Init(reader.Buffer, reader.Offset, (int)hsz);
            reader.Seek(reader.Position + (int)hsz);
            T obj = new T();
            obj.Init(r);
            readSize += hsz;
            return obj;
        }

        public List<T> read_obj_list<T>() where T : SprotoTypeBase, new()
        {
            UInt32 sz = ReadArraySize();
            var list = new List<T>();
            var r = new SprotoTypeReader();
            while (sz > 0)
            {
                list.Add(ReadElement<T>(r, sz, out UInt32 readSize));
                sz -= readSize;
            }
            return list;
        }

        public delegate TK GenKeyFunc<TK, TV>(TV v);

        public Dictionary<TK, TV> read_map<TK, TV>(GenKeyFunc<TK, TV> func) where TV : SprotoTypeBase, new()
        {
            UInt32 sz = ReadArraySize();
            var map = new Dictionary<TK, TV>();
            var r = new SprotoTypeReader();
            while (sz > 0)
            {
                TV v = ReadElement<TV>(r, sz, out UInt32 readSize);
                TK k = func(v);
                map.Add(k, v);
                sz -= readSize;
            }
            return map;
        }

        public void read_unknow_data()
        {
            if (value < 0)
            {
                int sz = (int)ReadDword();
                reader.Seek(sz + reader.Position);
            }
        }

        public int size() => reader.Position;

        public void Clear()
        {
            fn = 0;
            tag = -1;
            value = 0;
            reader?.Seek(0);
        }
    }
}
