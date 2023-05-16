using System;
using System.Threading;
using Spiricon.Automation;
using Aerotech.A3200;
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

            Console.WriteLine("Set up the beam camera as usual. Type\n\nStartCalibration\n\nto continue.");
            Console.ReadLine();

            A3200Connection.RunProgram();
            // Run doesn't halt program execution.
            
            // Event loop to start/stop measurements controlled by the aerotech program.
            while(true){
                
                break;
            }
            
            A3200Connection.Disconnect();

        }
    }

    public class BeamGageConnector
    {
        // Declare the BeamGage Automation client
        private AutomatedBeamGage _bgClient;

        public void Connect(){
            Console.WriteLine("Press enter to exit.\n");
            // Start BeamGage Automation client
            _bgClient = new AutomatedBeamGage("ScannerCalibration", true);
        }

        public void Disconnect(){
            // Shutdown BeamGage
            _bgClient.Instance.Shutdown();
        }
    }

    public class A3200Connector
    {
        private Controller controller = null;
        public bool MeasureActive {
            get{ return ReadMeasureActive(); }
        }

        public void Connect(){
            controller = Controller.Connect();
            if (controller != null){
                Console.WriteLine("Controller connected.");
            }
            else{
                Console.WriteLine("Controller could not connect! Retry? Type y for yes or n for no");
                string retry = Console.ReadLine();
                switch (retry){
                    case "y":
                        Connect();
                        break;
                    case "n":
                        break;
                    default:
                        break;
                }
                return;
            }
        }
        
        public void Disconnect(){
            Controller.Disconnect();
            controller.Dispose();
            controller = null;
            Console.WriteLine("Controller disconnected.");
        }

        public void RunProgram(){
            controller.Tasks.StopPrograms();
            Task task = controller.Tasks[TaskId.T01];
            task.Program.Run("CalibrationWithBeamGage.pgm");
            Thread.Sleep(1000);
        }

        private bool ReadMeasureActive(){
            int variable = controller.Variables.Tasks[TaskId.T01]["$MeasureActive"].Number;
            // TODO Exception if $MeasureActive doesn't exist.
            return Convert.ToBoolean(variable);
        }

        
    }
}
