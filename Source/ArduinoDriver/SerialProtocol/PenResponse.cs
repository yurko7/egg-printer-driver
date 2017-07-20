namespace ArduinoDriver.SerialProtocol
{
    public class PenResponse : ArduinoResponse
    {
        public PenState PenState { get; private set; }

        public PenResponse(PenState penState)
        {
            PenState = penState;
        }
    }
}
