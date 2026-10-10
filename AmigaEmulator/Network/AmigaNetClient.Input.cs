namespace AmigaEmulator.Network;

public sealed partial class AmigaNetClient
{
    private int _inputConfiguration;
    public int InputConfiguration => System.Threading.Volatile.Read(ref _inputConfiguration);
    public byte HostInputMode { get; private set; }

    private void PublishInputConfiguration()
    {
        System.Threading.Volatile.Write(ref _inputConfiguration, HostInputMode | ((int)Permission << 8)
            | (KeyboardEnabled ? 1 << 16 : 0) | (MouseEnabled ? 1 << 17 : 0));
    }

    public void SendMouseInput(int dx, int dy, byte buttons)
    {
        if (IsConnected && MouseEnabled)
            SendMessage(new AmigaNetMessage { Type = AmigaNetMessageType.MouseInput, Timestamp = System.DateTime.UtcNow.Ticks, Payload = AmigaNetProtocol.CreateMouseInputPayload(dx, dy, buttons) });
    }
}
