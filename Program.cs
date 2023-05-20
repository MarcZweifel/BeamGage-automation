using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
            // A3200Connector A3200Connection = new A3200Connector();
            // A3200Connection.Connect();
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
                    
                    BGConnection.Measure(2000);
                    break;
                }
                else if (answer == "cancel")
                {
                    // Terminate the program
                    // A3200Connection.Disconnect();
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
            // CalibrationProgram Calibration = new CalibrationProgram(BGConnection, A3200Connection);
            // Calibration.RunProgram();
            // A3200Connection.Disconnect();
            BGConnection.Disconnect();

        }
    }

    public class BeamGageConnector
    {
        /**
        Class to handle the connection to BeamGage.
        **/
        // Declare the BeamGage Automation client
        private AutomatedBeamGage bgClient;
        private bool MeasureOn = false;
        private List<double> ResultX = new List<double>();
        private List<double> ResultY = new List<double>();

        public void Connect()
        {
            ///Starts a new BeamGage automation client, lets user know over the console and shows the GUI.
            bgClient = new AutomatedBeamGage("ScannerCalibration", true);
            new AutomationFrameEvents(bgClient.ResultsPriorityFrame).OnNewFrame += OnFrameFunction;
            Console.WriteLine("BeamGage connected.");
        }
        
        private void OnFrameFunction()
        {
            if (MeasureOn)
            {
                ResultX.Add(bgClient.SpatialResults.CentroidX);
                ResultY.Add(bgClient.SpatialResults.CentroidY);
            }
        }

        public void Disconnect()
        {
            ///Shut down the BeamGage automation client.
            bgClient.Instance.Shutdown();
            Console.WriteLine("BeamGage disconnected.");
        }

        public void Measure(int MillisecondsDuration)
        {
            ///Controls the measurement routine. Duration is in seconds.
            //TODO Calculate and return mean & std. deviation, then clear result list.
            Console.WriteLine("Measurement started.");
            MeasureOn = true;
            Thread.Sleep(MillisecondsDuration);
            MeasureOn = false;
            Console.WriteLine("Measurement ended.");
            Console.WriteLine("X\t\t\t\tY");
            for (int i=0; i<ResultX.Count; i++)
            {
                Console.WriteLine(ResultX[i] + "\t\t" + ResultY[i]);
            }
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
            string[,] GridVariables = GetGridVariables();
            NumU = GetVariable<int>("NumU", GridVariables);
            NumV = GetVariable<int>("NumV", GridVariables);
            DeltaU = GetVariable<double>("DeltaU", GridVariables);
            DeltaV = GetVariable<double>("DeltaV", GridVariables);
            MeasureDuration = GetVariable<double>("MeasureDuration", GridVariables);
            Results = new double[2, NumU, NumV];
            Aerotech = AerotechConnector;
            BeamGage = BeamGageConnector;
        }

        private string[,] GetGridVariables()
        {
            /**
            Reads the lines contained in < and > in GridConfiguration.txt and reads the variables from them.
            **/

            // Dynamic lists for line reading
            List<string> lines = new List<string>();
            
            using (FileStream fs = File.OpenRead("GridConfiguration.txt"))
            {
                List<char> line = new List<char>();
                bool ReadLine = false;
                
                // Iterate over the whole file stream
                while (fs.Position != fs.Length)
                {
                    char character = Convert.ToChar(fs.ReadByte());
                    // Beginning of line
                    if (character=='<')
                    {
                        ReadLine = true;
                        continue;
                    }
                    // End of line
                    else if (character=='>')
                    {
                        ReadLine = false;
                        lines.Add(new string(line.ToArray()));
                        line.Clear();
                        continue;
                    }
                    // Add read character to line
                    if (ReadLine)
                    {
                        line.Add(character);
                    }
                }
            }
            string [,] result = new string[lines.Capacity,2];
            for (int i=0; i<lines.Count; i++)
            {
                string[] temp = lines[i].Split('=');
                result[i,0] = temp[0].Trim();
                result[i,1] = temp[1].Trim();

            }
            return result;
        }
    
        private dynamic GetVariable<T>(string name, string[,] variables)
        {
            string result = "";
            for (int i=0; i<variables.GetLength(0); i++)
            {
                if (variables[i,0] == name)
                {
                    result = variables[i,1];
                    break;
                }
            }
            
            if (typeof(T)==typeof(int))
            {
                int temp = Convert.ToInt32(result);
                Console.WriteLine(name + " is " + temp + " of type " + typeof(int));
                return temp;
            }
            else if (typeof(T)==typeof(double))
            {
                double temp = Convert.ToDouble(result);
                Console.WriteLine(name + " is " + temp + " of type " + typeof(double));
                return temp;
            }
            else
            {
                Console.WriteLine(name + " is " + result + " of type " + typeof(string));
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