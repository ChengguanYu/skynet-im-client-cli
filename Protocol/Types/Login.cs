using Sproto;

namespace SprotoType
{
    /// <summary>
    /// login 协议（proto.sproto 定义）。
    /// 字段 0: account (string), 字段 1: password (string)。
    /// </summary>
#pragma warning disable CS8981 // 类型名称仅含小写 ascii，sproto 协议名规范如此
    public class login : SprotoTypeBase
    {
        private const int MaxFieldCount = 2;
        
        public string account;
        
        public string password;
        
        public login(string account, string password) : base(MaxFieldCount)
        {
            this.account = account;                                                                                           
            this.password = password;                                                                                         
            has_field.set_field(0, true);                                                                                     
            has_field.set_field(1, true);            
        }

        public override int encode(SprotoStream stream)
        {
            serialize.open(stream);                                                                                           
            serialize.write_string(account, 0);                                                                               
            serialize.write_string(password, 1);                                                                              
            return serialize.close();   
        }

        protected override void Decode()
        {
            int fieldTag;
            while ((fieldTag = deserialize.read_tag()) >= 0)
            {
                switch (fieldTag)
                {
                    case 0: account  = deserialize.read_string(); break;                                                                  
                    case 1: password = deserialize.read_string(); break;   
                }
            }
        }
    }
}
