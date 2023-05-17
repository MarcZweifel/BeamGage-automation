using System;
using System.Threading;
using System.Configuration;
using Spiricon.Automation;
using Aerotech.A3200;
using Aerotech.A3200.Commands;
using Aerotech.A3200.Variables;
using Aerotech.A3200.Tasks;

namespace BeamGageAutomation
{
    class Program
    {
        static void Main(string[] args)
        {
            // Connect to the two software APIs using custom connector classes.
            A3200Connector A3200Connection = new A3200Connector();
            A3200Connection.Connect();
            BeamGageConnector BGConnection = new BeamGageConnector();
            BGConnection.Connect();
            
            Console.WriteLine("Set up the beam camera as usual.");
            Console.WriteLine("Set grid parameters in App.config.");
            Console.WriteLine("The current location will be the zero point of the calibration.");
            Console.WriteLine("Type start to start the calibration and cancel to terminate the program.");
            
            
            string answer = Console.ReadLine().ToLower();
            while (true)
            {
                if (answer == "start")
                {
                    // Start the calibration program
                    break;
                }
                else if (answer == "cancel")
                {
                    // Terminate the program
                    A3200Connection.Disconnect();
                    BGConnection.Disconnect();
                    Environment.Exit(0);
                }
                else
                {
                    // Notify that invalid input. Try again
                    Console.WriteLine("Invalid command! Set up the beam camera as usual. Type start to start the calibration and cancel to terminate the program.");
                    answer = Console.ReadLine().ToLower();
                }
            }
            CalibrationProgram Calibration = new CalibrationProgram(BGConnection, A3200Connection);
            Calibration.RunProgram();
            A3200Connection.Disconnect();

        }
    }

    public class BeamGageConnector
    {
        /**
        Class to handle the connection to BeamGage.
        **/
        // Declare the BeamGage Automation client
        private AutomatedBeamGage bgClient;

        public void Connect()
        {
            ///Starts a new BeamGage automation client, lets user know over the console and shows the GUI.
            bgClient = new AutomatedBeamGage("ScannerCalibration", true);
            Console.WriteLine("BeamGage connected.");
        }

        public void Disconnect()
        {
            ///Shut down the BeamGage automation client.
            bgClient.Instance.Shutdown();
        }

        public void Measure(double SecondsDuration)
        {
            ///Controls the measurement routine. Duration is in seconds.
            //TODO Implement measurement routine
            Console.WriteLine("Measurement started.");
            Thread.Sleep(Convert.ToInt32(SecondsDuration)*1000);
            Console.WriteLine("Measurement ended.");
        }
    }

    public class A3200Connector
    {
        /**
        Class to handle the connection to Aerotech A3200.
        **/
        private Controller controller;

        public void Connect()
        {
            ///Connects to the A3200 controller.
            controller = Controller.Connect();
            controller.Commands.Motion.Linear(new string[] {"U", "V"}, new double[] {0, 0});
            Console.WriteLine("Controller connected.");
        }

        public void Disconnect()
        {
            ///Disconnects from A3200 controller and does some data clean up.
            Controller.Disconnect();
            controller.Dispose();
            Console.WriteLine("Controller disconnected.");
        }

        public void SetZero()
        {
            ///Function to set the Zero point of the machine coordinate system.
            controller.Commands.Motion.Setup.PosOffsetSet(new string[]{"X", "Y", "U", "V"}, new double[]{0, 0, 0, 0});
        }

        public void MoveToAbs(double UCoord, double VCoord)
        {
            ///Function to move optical and mechanical axes to the measurement point in absolute machine coordinates.
            controller.Commands.Motion.Setup.Absolute();
            controller.Commands.Motion.Linear(new string[] {"U", "V", "X", "Y"}, new double[] {UCoord, VCoord, -UCoord, -VCoord}, 20);
            Thread.Sleep(500); // milliseconds of settling time for axis vibration.
        }
    }

    public class CalibrationProgram
    {
        /**
        Class which contains the functionality of the Aerotech measurment program.
        **/
        public double [,,] Results;
        private static int NumU;
        private static int NumV;
        private static double DeltaU;
        private static double DeltaV;
        private static double MeasureDuration;
        private BeamGageConnector BeamGage;
        private A3200Connector Aerotech;
        
        public CalibrationProgram(BeamGageConnector BeamGageConnector, A3200Connector AerotechConnector)
        {
            ///Constructor reads the grid and measurement parameters from the configuration file.
            NumU = GetGridVariable<int>("NumU");
            NumV = GetGridVariable<int>("NumV");
            DeltaU = GetGridVariable<double>("DeltaU");
            DeltaV = GetGridVariable<double>("DeltaV");
            MeasureDuration = GetGridVariable<double>("MeasureDuration");
            Results = new double[2, NumU, NumV];
            Aerotech = AerotechConnector;
            BeamGage = BeamGageConnector;
        }

        private dynamic GetGridVariable<T>(string name)
        {
            /**
            Reads Variable "name" from App.config and returns it as type <T>.
            <T> can be either int or double. Otherwise variable is returned as a string.
            **/
            // TODO Change to system with .txt files.
            string result = "";
            Console.WriteLine(name + " is " + result);
            if (typeof(T) == typeof(int))
            {
                return Convert.ToInt32(result);
            }
            else if (typeof(T) == typeof(double))
            {
                return Convert.ToDouble(result);
            }
            else
            {
                return result;
            }
        }
    
        public void RunProgram()
        {
            Aerotech.SetZero();
            for (int i=0; i<NumV; i++)
            {
                for (int j=0; j<NumU; j++)
                {
                    int IdxU;
                    if (i%2==0)
                    {
                        IdxU = j;
                    }
                    else
                    {
                        IdxU = NumU-1-j;
                    }
                    int IdxV = i;
                    double UCoord = (IdxU-(NumU-1)/2)*DeltaU;
                    double VCoord = ((NumV-1)/2-IdxV)*DeltaV;
                    Aerotech.MoveToAbs(UCoord, VCoord);
                    BeamGage.Measure(2);
                }
            }
        }
    
    }

}