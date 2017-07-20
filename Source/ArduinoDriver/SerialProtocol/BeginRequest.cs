namespace ArduinoDriver.SerialProtocol
{
    public class BeginRequest : ArduinoRequest
    {
        public BeginRequest()
            : base(CommandConstants.Begin)
        {
        }
    }
}
