#nullable disable
using System.IO;

namespace Sproto
{
    public abstract class SprotoTypeBase
    {
        protected SprotoTypeFieldOP has_field;
        protected SprotoTypeSerialize serialize;
        protected SprotoTypeDeserialize deserialize;

        protected SprotoTypeBase(int maxFieldCount)
        {
            has_field = new SprotoTypeFieldOP(maxFieldCount);
            serialize = new SprotoTypeSerialize(maxFieldCount);
            deserialize = new SprotoTypeDeserialize();
        }

        public int Init(byte[] buffer, int offset = 0)
        {
            Clear();
            deserialize.init(buffer, offset);
            Decode();
            return deserialize.size();
        }

        public long Init(SprotoTypeReader reader)
        {
            Clear();
            deserialize.init(reader);
            Decode();
            return deserialize.size();
        }

        public abstract int encode(SprotoStream stream);

        public byte[] Encode()
        {
            var stream = new SprotoStream();
            encode(stream);
            int len = stream.Position;

            byte[] result = new byte[len];
            stream.Seek(0, SeekOrigin.Begin);
            stream.Read(result, 0, len);
            return result;
        }

        protected abstract void Decode();

        public void Clear()
        {
            has_field.clear_field();
            deserialize.Clear();
        }
    }
}
