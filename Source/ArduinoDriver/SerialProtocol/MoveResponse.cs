namespace ArduinoDriver.SerialProtocol
{
    public class MoveResponse : ArduinoResponse
    {
        public int NumberOfPoints { get; private set; }

        public MoveResponse(byte numberOfPoints)
        {
            NumberOfPoints = numberOfPoints;
        }
    }
}
