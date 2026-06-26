using Sproto;

namespace SprotoType
{
    /// <summary>
    /// register 协议（proto.sproto 定义）。
    /// 字段 0: password (string) — 注册用的密码，服务端分配 account。
    /// </summary>
#pragma warning disable CS8981
    public class register : SprotoTypeBase
    {
        private const int MaxFieldCount = 1;

        public string password;

        public register(string password) : base(MaxFieldCount)
        {
            this.password = password;
            has_field.set_field(0, true);
        }

        public override int encode(SprotoStream stream)
        {
            serialize.open(stream);
            serialize.write_string(password, 0);
            return serialize.close();
        }

        protected override void Decode()
        {
            int tag;
            while ((tag = deserialize.read_tag()) >= 0)
            {
                switch (tag)
                {
                    case 0: password = deserialize.read_string(); break;
                }
            }
        }
    }
}
