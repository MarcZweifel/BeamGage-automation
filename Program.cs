using System;
using Spiricon.Automation;


namespace BGCSharpExample
{
    class Program
    {
        static void Main(string[] args)
        {
            Runner runMe = new Runner();
            runMe.Run();
            
        }
    }

    public class Runner
    {
        // Declare the BeamGage Automation client
        private AutomatedBeamGage _bg;

        public void Run()
        {
            Console.WriteLine("Press enter to exit.\n");
            // Start BeamGage Automation client
            _bg = new AutomatedBeamGage("ClientOne", false);

            

            // Create and register for the new frame event
            //new AutomationFrameEvents(_bg.ResultsPriorityFrame).OnNewFrame += NewFrameFunction;
            
            
            
            Console.Read();

            // Shutdown BeamGage
            _bg.Instance.Shutdown();
        }
    }
}
