using Sproto;

namespace SprotoType
{
    /// <summary>
    /// sproto RPC 信封头（.package 定义）。
    /// 字段 0: type (integer) — 协议号（对应 login 1 的 "1"）
    /// 字段 1: session (integer) — 请求/响应配对 ID
    /// </summary>
#pragma warning disable CS8981 // 类型名称仅含小写 ascii，sproto 协议名规范如此
    public class package : SprotoTypeBase
    {
        private const int MaxFieldCount = 2;

        public int type;
        public int session;

        /// <summary>用于 Decode 路径的无参构造。</summary>
        public package() : base(MaxFieldCount) { }

        /// <summary>用于 Encode 路径的有参构造。</summary>
        public package(int type, int session) : base(MaxFieldCount)
        {
            this.type = type;
            this.session = session;
            has_field.set_field(0, true);
            has_field.set_field(1, true);
        }

        public override int encode(SprotoStream stream)
        {
            serialize.open(stream);
            serialize.write_integer(type, 0);
            serialize.write_integer(session, 1);
            return serialize.close();
        }

        protected override void Decode()
        {
            int tag;
            while ((tag = deserialize.read_tag()) >= 0)
            {
                switch (tag)
                {
                    case 0: type = (int)deserialize.read_integer(); break;
                    case 1: session = (int)deserialize.read_integer(); break;
                }
            }
        }
    }
}
