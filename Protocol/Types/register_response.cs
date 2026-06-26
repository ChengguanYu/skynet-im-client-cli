using Sproto;

namespace SprotoType
{
    /// <summary>
    /// register 协议的 response（proto.sproto 定义）。
    /// 字段 0: account (string) — 服务端分配的账号。
    /// </summary>
#pragma warning disable CS8981
    public class register_response : SprotoTypeBase
    {
        private const int MaxFieldCount = 1;

        public string account = string.Empty;

        public register_response() : base(MaxFieldCount) { }

        public static register_response FromBytes(byte[] data)
        {
            var msg = new register_response();
            msg.Init(data);
            return msg;
        }

        public override int encode(SprotoStream stream)
        {
            serialize.open(stream);
            serialize.write_string(account, 0);
            return serialize.close();
        }

        protected override void Decode()
        {
            int tag;
            while ((tag = deserialize.read_tag()) >= 0)
            {
                switch (tag)
                {
                    case 0: account = deserialize.read_string(); break;
                }
            }
        }
    }
}
