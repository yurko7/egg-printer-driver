using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArduinoDriver.SerialProtocol;
using ArduinoUploader;
using ArduinoUploader.Hardware;
using NLog;
using RJCP.IO.Ports;

namespace ArduinoDriver
{
    /// <summary>
    /// An ArduinoDriver can be used to communicate with an attached Arduino by way of sending requests and receiving responses.
    /// These messages are sent over a live serial connection (via a serial protocol) to the Arduino. The required listener can be 
    /// automatically deployed to the Arduino.
    /// </summary>
    public class ArduinoDriver : IDisposable
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly IList<ArduinoModel> alwaysRedeployListeners = 
            new List<ArduinoModel> { ArduinoModel.NanoR3 };
        private const int defaultRebootGraceTime = 2000;
        private readonly IDictionary<ArduinoModel, int> rebootGraceTimes = 
            new Dictionary<ArduinoModel, int>
            {
                {ArduinoModel.Micro, 8000},
                {ArduinoModel.Mega2560, 4000}
            };
        private const int CurrentProtocolMajorVersion = 1;
        private const int CurrentProtocolMinorVersion = 0;
        private const int DriverBaudRate = 115200;
        private ArduinoDriverSerialPort port;
        private ArduinoDriverConfiguration config;
        private Func<SerialPortStream> serialFunc;

        private const string ArduinoListenerHexResourceFileName =
            "ArduinoDriver.ArduinoListener.ArduinoListener.ino.{0}.hex";

        private ArduinoDriver()
        { }

        /// <summary>
        /// Creates a new ArduinoDriver instance. The relevant portname will be autodetected if possible.
        /// </summary>
        /// <param name="arduinoModel"></param>
        /// <param name="autoBootstrap"></param>
        public static async Task<ArduinoDriver> CreateAsync(ArduinoModel arduinoModel, bool autoBootstrap = false)
        {
            logger.Info(
                "Instantiating ArduinoDriver (model {0}) with autoconfiguration of port name...",
                arduinoModel);

            var possiblePortNames = SerialPortStream.GetPortNames().Distinct();
            string unambiguousPortName = null;
            try
            {
                unambiguousPortName = possiblePortNames.SingleOrDefault();
            }
            catch (InvalidOperationException)
            {
                // More than one posible hit.
            }
            if (unambiguousPortName == null)
                throw new IOException(
                    "Unable to autoconfigure ArduinoDriver port name, since there is not exactly a single "
                    + "COM port available. Please use the ArduinoDriver with the named port constructor!");

            var arduinoDriver = new ArduinoDriver();
            await arduinoDriver.InitializeAsync(new ArduinoDriverConfiguration
            {
                ArduinoModel = arduinoModel, 
                PortName = unambiguousPortName, 
                AutoBootstrap = autoBootstrap
            });
            return arduinoDriver;
        }

        /// <summary>
        /// Creates a new ArduinoDriver instance for a specified portName.
        /// </summary>
        /// <param name="arduinoModel"></param>
        /// <param name="portName">The COM portname to create the ArduinoDriver instance for.</param>
        /// <param name="autoBootstrap">Determines if an listener is automatically deployed to the Arduino if required.</param>
        public static async Task<ArduinoDriver> CreateAsync(ArduinoModel arduinoModel, string portName, bool autoBootstrap = false)
        {
            var arduinoDriver = new ArduinoDriver();
            await arduinoDriver.InitializeAsync(new ArduinoDriverConfiguration
            {
                ArduinoModel = arduinoModel,
                PortName = portName,
                AutoBootstrap = autoBootstrap
            });
            return arduinoDriver;
        }

        /// <summary>
        /// Sends a Begin Request to the Arduino.
        /// </summary>
        /// <param name="request">Begin Request</param>
        /// <returns>The Begin Response</returns>
        public async Task<BeginResponse> SendAsync(BeginRequest request)
        {
            return (BeginResponse) await InternalSendAsync(request);
        }

        /// <summary>
        /// Sends a End Request to the Arduino.
        /// </summary>
        /// <param name="request">End Request</param>
        /// <returns>The End Response</returns>
        public async Task<EndResponse> SendAsync(EndRequest request)
        {
            return (EndResponse) await InternalSendAsync(request);
        }

        /// <summary>
        /// Sends a Pen Request to the Arduino.
        /// </summary>
        /// <param name="request">Pen Request</param>
        /// <returns>The Pen Response</returns>
        public async Task<PenResponse> SendAsync(PenRequest request)
        {
            return (PenResponse) await InternalSendAsync(request);
        }

        /// <summary>
        /// Sends a Move Request to the Arduino.
        /// </summary>
        /// <param name="request">Move Request</param>
        /// <returns>The Move Response</returns>
        public async Task<MoveResponse> SendAsync(MoveRequest request)
        {
            return (MoveResponse) await InternalSendAsync(request);
        }

        /// <summary>
        /// Sends a Dot Request to the Arduino.
        /// </summary>
        /// <param name="request">Dot Request</param>
        /// <returns>The Dot Response</returns>
        public async Task<DotResponse> SendAsync(DotRequest request)
        {
            return (DotResponse) await InternalSendAsync(request);
        }

        /// <summary>
        /// Sends a Line Request to the Arduino.
        /// </summary>
        /// <param name="request">Line Request</param>
        /// <returns>The Line Response</returns>
        public async Task<LineResponse> SendAsync(LineRequest request)
        {
            return (LineResponse) await InternalSendAsync(request);
        }

        /// <summary>
        /// Disposes the ArduinoDriver instance.
        /// </summary>
        public void Dispose()
        {
            try
            {
                port.Close();
                port.Dispose();
            }
            catch (Exception)
            {
                // Ignore
            }
        }

        #region Private Methods

        private Task InitializeAsync(ArduinoDriverConfiguration arduinoConfig)
        {
            logger.Info("Instantiating ArduinoDriver: {0} - {1}...", arduinoConfig.ArduinoModel, arduinoConfig.PortName);

            config = arduinoConfig;
            serialFunc = () => new SerialPortStream() { PortName = config.PortName, BaudRate = DriverBaudRate };

            return config.AutoBootstrap
                ? InitializeWithAutoBootstrapAsync()
                : InitializeWithoutAutoBootstrapAsync();
        }

        private async Task InitializeWithoutAutoBootstrapAsync()
        {
            // Without auto bootstrap, we just try to send a handshake request (the listener should already be
            // deployed). If that fails, we try nothing else.
            logger.Info("Initiating handshake...");
            InitializePort();
            var handshakeResponse = await ExecuteHandshakeAsync();
            if (handshakeResponse == null)
            {
                port.Close();
                port.Dispose();
                throw new IOException(
                    string.Format(
                        "Unable to get a handshake ACK when sending a handshake request to the Arduino on port {0}. "
                        +
                        "Pass 'true' for optional parameter autoBootStrap in one of the ArduinoDriver constructors to "
                        + "automatically configure the Arduino (please note: this will overwrite the existing sketch "
                        + "on the Arduino).", config.PortName));
            }
        }

        private async Task InitializeWithAutoBootstrapAsync()
        {
            var alwaysReDeployListener = alwaysRedeployListeners.Count > 500;
            HandShakeResponse handshakeResponse = null;
            var handShakeAckReceived = false;
            var handShakeIndicatesOutdatedProtocol = false;
            if (!alwaysReDeployListener)
            {
                logger.Info("Initiating handshake...");
                InitializePort();
                handshakeResponse = await ExecuteHandshakeAsync();
                handShakeAckReceived = handshakeResponse != null;
                if (handShakeAckReceived)
                {
                    logger.Info("Handshake ACK Received ...");
                    const int currentVersion = (CurrentProtocolMajorVersion * 10) + CurrentProtocolMinorVersion;
                    var listenerVersion = (handshakeResponse.ProtocolMajorVersion * 10) + handshakeResponse.ProtocolMinorVersion;
                    logger.Info("Current ArduinoDriver C# Protocol: {0}.{1}.", 
                        CurrentProtocolMajorVersion, CurrentProtocolMinorVersion);
                    logger.Info("Arduino Listener Protocol Version: {0}.{1}", 
                        handshakeResponse.ProtocolMajorVersion, handshakeResponse.ProtocolMinorVersion);
                    handShakeIndicatesOutdatedProtocol = currentVersion > listenerVersion;
                    if (handShakeIndicatesOutdatedProtocol)
                    {
                        logger.Debug("Closing port...");
                        port.Close();
                        port.Dispose();                        
                    }
                }
                else
                {
                    logger.Debug("Closing port...");
                    port.Close();
                    port.Dispose();
                }
            }

            // If we have received a handshake ack, and we have no need to upgrade, simply return.
            if (handShakeAckReceived && !handShakeIndicatesOutdatedProtocol) return;

            var arduinoModel = config.ArduinoModel;
            // At this point we will have to redeploy our listener
            logger.Info("Boostrapping ArduinoDriver Listener...");
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            ExecuteAutoBootStrap(arduinoModel, config.PortName);
            stopwatch.Stop();
            logger.Info("Bootstrapping device took {0}ms.", stopwatch.ElapsedMilliseconds);

            // Now wait a bit, since the bootstrapped Arduino might still be restarting !
            var graceTime =
                rebootGraceTimes.ContainsKey(arduinoModel) ?
                    rebootGraceTimes[arduinoModel] : defaultRebootGraceTime;
            logger.Info("Grace time after reboot, waiting {0}ms...", graceTime);
            Thread.Sleep(graceTime);


            // Listener should now (always) be deployed, handshake should yield success.
            InitializePort();
            handshakeResponse = await ExecuteHandshakeAsync();
            if (handshakeResponse == null)
                throw new IOException("Unable to get a handshake ACK after executing auto bootstrap on the Arduino!");  
            logger.Info("ArduinoDriver fully initialized!");      
        }

        private void InitializePort()
        {
            var serialEngine = serialFunc();
            var portName = serialEngine.PortName;
            var baudRate = serialEngine.BaudRate;
            logger.Debug("Initializing port {0} - {1}...", portName, baudRate);
            serialEngine.WriteTimeout = 200;
            serialEngine.ReadTimeout = 500;
            port = new ArduinoDriverSerialPort(serialEngine);
            port.Open();
        }

        private Task<ArduinoResponse> InternalSendAsync(ArduinoRequest request)
        {
            return port.SendAsync(request);
        }

        private async Task<HandShakeResponse> ExecuteHandshakeAsync()
        {
            var response = await port.SendAsync(new HandShakeRequest(), 1);
            return response as HandShakeResponse;            
        }

        private static void ExecuteAutoBootStrap(ArduinoModel arduinoModel, string portName)
        {
            logger.Info("Executing AutoBootStrap!");
            logger.Info("Deploying protocol version {0}.{1}.", CurrentProtocolMajorVersion, CurrentProtocolMinorVersion);
           
            logger.Debug("Reading internal resource stream with Arduino Listener HEX file...");
            var assembly = Assembly.GetExecutingAssembly();
            var textStream = assembly.GetManifestResourceStream(
                string.Format(ArduinoListenerHexResourceFileName, arduinoModel));
            if (textStream == null) 
                throw new IOException("Unable to configure auto bootstrap, embedded resource missing!");

            var hexFileContents = new List<string>();
            using (var reader = new StreamReader(textStream))
                while (reader.Peek() >= 0) hexFileContents.Add(reader.ReadLine());

            logger.Debug("Uploading HEX file...");
            var uploader = new ArduinoSketchUploader(new ArduinoSketchUploaderOptions
            {
                PortName = portName,
                ArduinoModel = arduinoModel
            });
            uploader.UploadSketch(hexFileContents);
        }

        #endregion
    }
}
