using Sproto;

namespace SprotoType
{
    /// <summary>
    /// login 协议的 response（proto.sproto 定义）。
    /// 字段 0: token (string) — 服务端返回的登录凭证。
    /// </summary>
#pragma warning disable CS8981 
    public class login_response : SprotoTypeBase
    {
        private const int MaxFieldCount = 1;

        public string token = string.Empty;

        public login_response() : base(MaxFieldCount) { }

        public static login_response FromBytes(byte[] data)
        {
            var msg = new login_response();
            msg.Init(data);
            return msg;
        }

        public override int encode(SprotoStream stream)
        {
            serialize.open(stream);
            serialize.write_string(token, 0);
            return serialize.close();
        }

        protected override void Decode()
        {
            int tag;
            while ((tag = deserialize.read_tag()) >= 0)
            {
                switch (tag)
                {
                    case 0: token = deserialize.read_string(); break;
                }
            }
        }
    }
}
