using System.Net.Sockets;
using Sproto;

namespace Im.Cli;

/// <summary>
/// 共享 TCP 会话，封装连接、发送 sproto 请求、读取响应的通用逻辑。
/// </summary>
public sealed class TcpSession : IDisposable
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly SprotoRpc _rpc;

    public TcpSession(SprotoRpc rpc)
    {
        _rpc = rpc;
    }

    /// <summary>
    /// 连接到远程主机，超时 10 秒。
    /// </summary>
    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        _tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        await _tcp.ConnectAsync(host, port, cts.Token);
        _stream = _tcp.GetStream();
    }

    /// <summary>
    /// 发送 sproto 请求并读取响应。
    /// 协议格式：2 字节大端长度前缀 + sproto_pack 数据。
    /// </summary>
    /// <param name="proto">协议名，如 "login"、"register"。</param>
    /// <param name="request">sproto request 对象。</param>
    /// <param name="session">RPC 会话 ID。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<RpcMessage> SendRequestAsync(string proto, SprotoObject request, long session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(_stream);

        // 打包请求（含 .package 头部 + body + sproto_pack）
        RpcPackage pkg = _rpc.PackRequest(proto, request, session);

        // 发送：2 字节大端长度前缀 + packed 数据
        byte[] packet = new byte[2 + pkg.size];
        packet[0] = (byte)((pkg.size >> 8) & 0xFF);
        packet[1] = (byte)(pkg.size & 0xFF);
        Array.Copy(pkg.data, 0, packet, 2, pkg.size);
        await _stream.WriteAsync(packet, ct);

        // 读取响应：2 字节大端长度前缀 + packed 数据
        byte[] lenBuf = new byte[2];
        await _stream.ReadExactlyAsync(lenBuf, 0, 2, ct);
        int respLen = (lenBuf[0] << 8) | lenBuf[1];
        byte[] packedResp = new byte[respLen];
        await _stream.ReadExactlyAsync(packedResp, 0, respLen, ct);

        return _rpc.UnpackMessage(packedResp, respLen);
    }

    public void Dispose()
    {
        _stream = null;
        _tcp?.Dispose();
    }
}
