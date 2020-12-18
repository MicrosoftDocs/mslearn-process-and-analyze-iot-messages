// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// This application uses the Azure IoT Hub device SDK for .NET
// For samples see: https://github.com/Azure/azure-iot-sdk-csharp/tree/master/iothub/device/samples

using System;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using System.Text;
using System.Threading.Tasks;

namespace ContainerSimulation
{
    class Program
    {

        // The three connection string for the different IoT Devices being simulated (Truck, Airplane, Container)

        private readonly static string connectionStringTruck = "{Your Truck device connection string here}";
        private readonly static string connectionStringAirplane = "{Your Airplane device connection string here}";
        private readonly static string connectionStringContainer = "{Your Container device connection string here}";


        // The DeviceClient's for the three different IoT Devices being simulated
        private static DeviceClient deviceClientTruck;
        private static DeviceClient deviceClientAirplane;
        private static DeviceClient deviceClientContainer;


        private static void Main(string[] args)
        {
            Console.WriteLine("Container Simulation");
            Console.WriteLine("This app simulations Temperature and Humidity sensors from the following devices:");
            Console.WriteLine(" - Container: The shipping container.");
            Console.WriteLine(" - Truck: The truck transporting the container.");
            Console.WriteLine(" - Airplane: The airplane transporting the container.");
            Console.WriteLine(string.Empty);
            Console.WriteLine("The Container is being shipped via Truck and Airplane, and the container sensor readings will vary depending on which transport vehicle is currently transporting the container.");
            Console.WriteLine(string.Empty);
            Console.WriteLine("Press Ctrl-C to exit.");
            Console.WriteLine(string.Empty);

            // Connect to the IoT hub using the MQTT protocol
            // Create a DeviceClient for each IoT Device being simulated
            deviceClientTruck = DeviceClient.CreateFromConnectionString(connectionStringTruck, TransportType.Mqtt);
            deviceClientAirplane = DeviceClient.CreateFromConnectionString(connectionStringAirplane, TransportType.Mqtt);
            deviceClientContainer = DeviceClient.CreateFromConnectionString(connectionStringContainer, TransportType.Mqtt);

            SendDeviceToCloudMessagesAsync();

            Console.ReadLine();
        }

        // Async method to send simulated telemetry
        private static async void SendDeviceToCloudMessagesAsync()
        {
            // configure the vehicle sensors with the appropriate ranges
            var truck = new Vehicle(
                temperatureMin: 20,
                temperatureMax: 40,
                humidityMin: 45,
                humidityMax: 65,
                initialTemperature: 20,
                initialHumidity: 60);

            var airplane = new Vehicle(
                temperatureMin: 0,
                temperatureMax: 25,
                humidityMin: 35,
                humidityMax: 50,
                initialTemperature: 15,
                initialHumidity: 45);

            var container = new Container(
                truck: truck,
                airplane: airplane,
                initialTemperature: 20,
                initialHumidity: 45);

            while (true)
            {
                // /////////////////////////////////////////////////////////////////////////////////////////////////
                // SEND SIMULATED TRUCK SENSOR TELEMETRY

                // Generate simulated Truck sensor readings
                var truckTemperature = truck.ReadTemperature();
                var truckHumidity = truck.ReadHumidity();

                // Create Truck JSON message
                var truckJson = CreateJSON(truckTemperature, truckHumidity);
                var truckMessage = CreateMessage(truckJson);

                // Send Truck telemetry message
                await deviceClientTruck.SendEventAsync(truckMessage);
                Console.WriteLine("{0} > Sending TRUCK message: {1}", DateTime.Now, truckJson);


                // /////////////////////////////////////////////////////////////////////////////////////////////////
                // SEND SIMULATED AIRPLANE SENSOR TELEMETRY

                // Generate simulated Airplane sensor readings
                var airplaneTemperature = airplane.ReadTemperature();
                var airplaneHumidity = airplane.ReadHumidity();

                // Create Airplane JSON message
                var airplaneJson = CreateJSON(airplaneTemperature, airplaneHumidity);
                var airplaneMessage = CreateMessage(airplaneJson);

                // Send Airplane telemetry message
                await deviceClientAirplane.SendEventAsync(airplaneMessage);
                Console.WriteLine("{0} > Sending AIRPLANE message: {1}", DateTime.Now, airplaneJson);


                // /////////////////////////////////////////////////////////////////////////////////////////////////
                // SEND SIMULATED CONTAINER SENSOR TELEMETRY

                // Automate changing transport every 30 seconds
                container.UpdateTransport();

                // Generate simulated Container sensor readings
                var containerTemperature = container.ReadTemperature();
                var containerHumidity = container.ReadHumidity();

                // Create Container JSON message
                var containerJson = CreateJSON(containerTemperature, containerHumidity);
                var containerMessage = CreateMessage(containerJson);

                // Send Container telemetry message
                await deviceClientContainer.SendEventAsync(containerMessage);
                Console.WriteLine("{0} > Sending CONTAINER message: {1}", DateTime.Now, containerJson);

                await Task.Delay(1000);
            }
        }

        static string CreateJSON(double temperature, double humidity)
        {
            var telemetry = new
            {
                temperature = temperature,
                humidity = humidity
            };

            return JsonConvert.SerializeObject(telemetry);
        }

        // Generate Telemetry message containing JSON data for the specified values
        static Message CreateMessage(string messageString)
        {
            var message = new Message(Encoding.ASCII.GetBytes(messageString));

            // MESSAGE CONTENT TYPE
            message.ContentType = "application/json";
            message.ContentEncoding = "UTF-8";

            return message;
        }
    }

    internal class Container : SensorBase
    {
        // Variables used to automate the change in Transport for the Container between Truck and Airplane
        private const double transportMaxDuration = 30; // 30 seconds
        private DateTime lastTransportChange = DateTime.Now;
        private bool containerTransportIsTruck = true;
        private Vehicle truck;
        private Vehicle airplane;

        internal Container(Vehicle truck, Vehicle airplane, double initialTemperature, double initialHumidity)
        {
            this.truck = truck;
            this.airplane = airplane;
            this.temperature = initialTemperature;
            this.humidity = initialHumidity;
        }

        internal void UpdateTransport()
        {
            TimeSpan transportDuration = DateTime.Now - lastTransportChange;

            // Change the transport every 30 seconds
            if (transportDuration.TotalSeconds > transportMaxDuration)
            {
                containerTransportIsTruck = !containerTransportIsTruck;
                lastTransportChange = DateTime.Now;
                Console.WriteLine("{0} > CONTAINER transport changed to: {1}", DateTime.Now, containerTransportIsTruck ? "TRUCK" : "AIRPLANE");
            }

            // Container Telemetry min/max thresholds
            TemperatureMin = containerTransportIsTruck ? truck.TemperatureMin : airplane.TemperatureMin;
            TemperatureMax = containerTransportIsTruck ? truck.TemperatureMax : airplane.TemperatureMax;
            HumidityMin = containerTransportIsTruck ? truck.HumidityMin : airplane.HumidityMin;
            HumidityMax = containerTransportIsTruck ? truck.HumidityMax : airplane.HumidityMax;
        }
    }

    internal class Vehicle : SensorBase
    {
        internal Vehicle(double temperatureMin, double temperatureMax, double humidityMin, double humidityMax, double initialTemperature, double initialHumidity)
        {
            TemperatureMin = temperatureMin;
            TemperatureMax = temperatureMax;
            HumidityMin = humidityMin;
            HumidityMax = humidityMax;
            temperature = initialTemperature;
            humidity = initialHumidity;
        }
    }

    // The Vehicle and Container classes inherit from this base class
    abstract class SensorBase
    {
        private static Random rand = new Random();

        // Sensor readings
        protected double temperature;
        protected double humidity;

        // Sensor ranges
        public double TemperatureMin { get; protected set; }
        public double TemperatureMax { get; protected set; }
        public double HumidityMin { get; protected set; }
        public double HumidityMax { get; protected set; }

        internal double ReadTemperature()
        {
            temperature = GenerateSensorReading(temperature, TemperatureMin, TemperatureMax);
            return temperature;
        }

        internal double ReadHumidity()
        {
            humidity = GenerateSensorReading(humidity, HumidityMin, HumidityMax);
            return humidity;
        }

        // Common sensor reading generator
        protected static double GenerateSensorReading(double currentValue, double min, double max)
        {
            double percentage = 5; // 5%

            // generate a new value based on the previous supplied value
            // The new value will be calculated to be within the threshold specified by the "percentage" variable from the original number.
            // The value will also always be within the the specified "min" and "max" values.
            double value = currentValue * (1 + ((percentage / 100) * (2 * rand.NextDouble() - 1)));

            value = Math.Max(value, min);
            value = Math.Min(value, max);

            return value;
        }
    }
}