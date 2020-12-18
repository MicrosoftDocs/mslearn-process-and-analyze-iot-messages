// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using System.Text;
using System.Threading.Tasks;

namespace VibrationDevice
{
    class Program
    {
        // Telemetry globals.
        private const int intervalInMilliseconds = 2000; // Time interval required by wait function.

        // IoT Hub global variables.
        private static DeviceClient deviceClient;

        // The device connection string to authenticate the device with your IoT hub.
        private readonly static string deviceConnectionString = "<your device connection string>";

        private static void Main(string[] args)
        {
            ConsoleHelper.WriteColorMessage("Vibration sensor device app.\n", ConsoleColor.Yellow);

            // Connect to the IoT hub using the MQTT protocol.
            deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);

            SendDeviceToCloudMessagesAsync();
            Console.ReadLine();
        }

        // Async method to send simulated telemetry.
        private static async void SendDeviceToCloudMessagesAsync()
        {
            var conveyor = new ConveyorBeltSimulator(intervalInMilliseconds);

            // Simulate the vibration telemetry of a conveyor belt.
            while (true)
            {
                var vibration = conveyor.ReadVibration();

                await CreateTelemetryMessage(conveyor, vibration);

                await CreateLoggingMessage(conveyor, vibration);

                await Task.Delay(intervalInMilliseconds);
            }
        }

        private static async Task CreateTelemetryMessage(ConveyorBeltSimulator conveyor, double vibration)
        {
            var telemetryDataPoint = new
            {
                vibration = vibration,
            };
            var telemetryMessageString = JsonConvert.SerializeObject(telemetryDataPoint);
            var telemetryMessage = new Message(Encoding.ASCII.GetBytes(telemetryMessageString));

            // Add a custom application property to the message. This is used to route the message.
            telemetryMessage.Properties.Add("sensorID", "VSTel");

            // Send an alert if the belt has been stopped for more than five seconds.
            telemetryMessage.Properties.Add("beltAlert", (conveyor.BeltStoppedSeconds > 5) ? "true" : "false");

            Console.WriteLine($"Telemetry data: {telemetryMessageString}");

            // Send the telemetry message.
            await deviceClient.SendEventAsync(telemetryMessage);
            ConsoleHelper.WriteGreenMessage($"Telemetry sent {DateTime.Now.ToShortTimeString()}");
        }

        private static async Task CreateLoggingMessage(ConveyorBeltSimulator conveyor, double vibration)
        {
            // Create the logging JSON message.
            var loggingDataPoint = new
            {
                vibration = Math.Round(vibration, 2),
                packages = conveyor.PackageCount,
                speed = conveyor.BeltSpeed.ToString(),
                temp = Math.Round(conveyor.Temperature, 2),
            };
            var loggingMessageString = JsonConvert.SerializeObject(loggingDataPoint);
            var loggingMessage = new Message(Encoding.ASCII.GetBytes(loggingMessageString));

            // Add a custom application property to the message. This is used to route the message.
            loggingMessage.Properties.Add("sensorID", "VSLog");

            // Send an alert if the belt has been stopped for more than five seconds.
            loggingMessage.Properties.Add("beltAlert", (conveyor.BeltStoppedSeconds > 5) ? "true" : "false");

            Console.WriteLine($"Log data: {loggingMessageString}");

            // Send the logging message.
            await deviceClient.SendEventAsync(loggingMessage);
            ConsoleHelper.WriteGreenMessage("Log data sent\n");
        }
    }

    internal class ConveyorBeltSimulator
    {
        Random rand = new Random();

        private readonly int intervalInSeconds;

        // Conveyor belt globals.
        public enum SpeedEnum
        {
            stopped,
            slow,
            fast
        }
        private int packageCount = 0;                                        // Count of packages leaving the conveyor belt.
        private SpeedEnum beltSpeed = SpeedEnum.stopped;                     // Initial state of the conveyor belt.
        private readonly double slowPackagesPerSecond = 1;                   // Packages completed at slow speed/ per second
        private readonly double fastPackagesPerSecond = 2;                   // Packages completed at fast speed/ per second
        private double beltStoppedSeconds = 0;                               // Time the belt has been stopped.
        private double temperature = 60;                                     // Ambient temperature of the facility.
        private double seconds = 0;                                          // Time conveyor belt is running.

        // Vibration globals.
        private double forcedSeconds = 0;                                    // Time since forced vibration started.
        private double increasingSeconds = 0;                                // Time since increasing vibration started.
        private double naturalConstant;                                      // Constant identifying the severity of natural vibration.
        private double forcedConstant = 0;                                   // Constant identifying the severity of forced vibration.
        private double increasingConstant = 0;                               // Constant identifying the severity of increasing vibration.

        public double BeltStoppedSeconds { get => beltStoppedSeconds; }
        public int PackageCount { get => packageCount; }
        public double Temperature { get => temperature; }
        public SpeedEnum BeltSpeed { get => beltSpeed; }

        internal ConveyorBeltSimulator(int intervalInMilliseconds)
        {

            // Create a number between 2 and 4, as a constant for normal vibration levels.
            naturalConstant = 2 + 2 * rand.NextDouble();
            intervalInSeconds = intervalInMilliseconds / 1000;  // Time interval in seconds.
        }

        internal double ReadVibration()
        {
            double vibration;

            // Randomly adjust belt speed.
            switch (beltSpeed)
            {
                case SpeedEnum.fast:
                    if (rand.NextDouble() < 0.01)
                    {
                        beltSpeed = SpeedEnum.stopped;
                    }
                    if (rand.NextDouble() > 0.95)
                    {
                        beltSpeed = SpeedEnum.slow;
                    }
                    break;

                case SpeedEnum.slow:
                    if (rand.NextDouble() < 0.01)
                    {
                        beltSpeed = SpeedEnum.stopped;
                    }
                    if (rand.NextDouble() > 0.95)
                    {
                        beltSpeed = SpeedEnum.fast;
                    }
                    break;

                case SpeedEnum.stopped:
                    if (rand.NextDouble() > 0.75)
                    {
                        beltSpeed = SpeedEnum.slow;
                    }
                    break;
            }

            // Set vibration levels.
            if (beltSpeed == SpeedEnum.stopped)
            {
                // If the belt is stopped, all vibration comes to a halt.
                forcedConstant = 0;
                increasingConstant = 0;
                vibration = 0;

                // Record how much time the belt is stopped, in case we need to send an alert.
                beltStoppedSeconds += intervalInSeconds;
            }
            else
            {
                // Conveyor belt is running.
                beltStoppedSeconds = 0;

                // Check for random starts in unwanted vibrations.

                // Check forced vibration.
                if (forcedConstant == 0)
                {
                    if (rand.NextDouble() < 0.1)
                    {
                        // Forced vibration starts.
                        forcedConstant = 1 + 6 * rand.NextDouble();             // A number between 1 and 7.
                        if (beltSpeed == SpeedEnum.slow)
                            forcedConstant /= 2;                                // Lesser vibration if slower speeds.
                        forcedSeconds = 0;
                        ConsoleHelper.WriteRedMessage($"Forced vibration starting with severity: {Math.Round(forcedConstant, 2)}");
                    }
                }
                else
                {
                    if (rand.NextDouble() > 0.99)
                    {
                        forcedConstant = 0;
                        ConsoleHelper.WriteGreenMessage("Forced vibration stopped");
                    }
                    else
                    {
                        ConsoleHelper.WriteRedMessage($"Forced vibration: {Math.Round(forcedConstant, 1)} started at: {DateTime.Now.ToShortTimeString()}");
                    }
                }

                // Check increasing vibration.
                if (increasingConstant == 0)
                {
                    if (rand.NextDouble() < 0.05)
                    {
                        // Increasing vibration starts.
                        increasingConstant = 100 + 100 * rand.NextDouble();     // A number between 100 and 200.
                        if (beltSpeed == SpeedEnum.slow)
                            increasingConstant *= 2;                            // Longer period if slower speeds.
                        increasingSeconds = 0;
                        ConsoleHelper.WriteRedMessage($"Increasing vibration starting with severity: {Math.Round(increasingConstant, 2)}");
                    }
                }
                else
                {
                    if (rand.NextDouble() > 0.99)
                    {
                        increasingConstant = 0;
                        ConsoleHelper.WriteGreenMessage("Increasing vibration stopped");
                    }
                    else
                    {
                        ConsoleHelper.WriteRedMessage($"Increasing vibration: {Math.Round(increasingConstant, 1)} started at: {DateTime.Now.ToShortTimeString()}");
                    }
                }

                // Apply the vibrations, starting with natural vibration.
                vibration = naturalConstant * Math.Sin(seconds);

                if (forcedConstant > 0)
                {
                    // Add forced vibration.
                    vibration += forcedConstant * Math.Sin(0.75 * forcedSeconds) * Math.Sin(10 * forcedSeconds);
                    forcedSeconds += intervalInSeconds;
                }

                if (increasingConstant > 0)
                {
                    // Add increasing vibration.
                    vibration += (increasingSeconds / increasingConstant) * Math.Sin(increasingSeconds);
                    increasingSeconds += intervalInSeconds;
                }
            }

            // Increment the time since the conveyor belt app started.
            seconds += intervalInSeconds;

            // Count the packages that have completed their journey.
            switch (beltSpeed)
            {
                case SpeedEnum.fast:
                    packageCount += (int)(fastPackagesPerSecond * intervalInSeconds);
                    break;

                case SpeedEnum.slow:
                    packageCount += (int)(slowPackagesPerSecond * intervalInSeconds);
                    break;

                case SpeedEnum.stopped:
                    // No packages!
                    break;
            }

            // Randomly vary ambient temperature.
            temperature += rand.NextDouble() - 0.5d;
            return vibration;
        }
    }

    internal static class ConsoleHelper
    {
        internal static void WriteColorMessage(string text, ConsoleColor clr)
        {
            Console.ForegroundColor = clr;
            Console.WriteLine(text);
            Console.ResetColor();
        }
        internal static void WriteGreenMessage(string text)
        {
            WriteColorMessage(text, ConsoleColor.Green);
        }

        internal static void WriteRedMessage(string text)
        {
            WriteColorMessage(text, ConsoleColor.Red);
        }
    }
}