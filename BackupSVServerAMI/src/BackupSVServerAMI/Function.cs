using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Amazon.Lambda.Core;
using System.Globalization;
using Amazon.EC2;
using Amazon.EC2.Model;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SVLambda
{
    public class SVAMIBackupFunction
    {
        static  IAmazonEC2 _amazonEC2 = new AmazonEC2Client();
        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task FunctionHandler(Amazon.Lambda.CloudWatchEvents.ScheduledEvents.ScheduledEvent event1, ILambdaContext context)
        {
            System.Console.WriteLine($"gotchaaas {Newtonsoft.Json.JsonConvert.SerializeObject(event1)}");
            // All log statements are written to CloudWatch by default. For more information, see
            // https://docs.aws.amazon.com/lambda/latest/dg/nodejs-prog-model-logging.html

            System.Console.WriteLine($"Account ID: {event1?.Account}, resources :{event1?.Resources[0]}");
            if (event1.Account != Environment.GetEnvironmentVariable("AccountId") ||
                event1.DetailType != "Scheduled Event" ||
                event1.Resources.Count !=1 ||
                //restricintg to between 8 and 10 UTC. 7 just to accomodate flex window
                (event1.Time.Hour < 7 || event1.Time.Hour >10) ||
                event1.Resources[0]!="arn:aws:events:us-west-2:822843877307:rule/BackupServerImagesNew")
                {
                    throw new Exception("Unexpected event encountered. QUitting processing");
                    // System.Console.WriteLine("Unexpected event encountered. QUitting processing.");
                    // return;
                }
            Dictionary<string,string> instanceDict = new Dictionary<string,string>();
            instanceDict.Add("SecureVideoHubQA",Environment.GetEnvironmentVariable("SecureVideoHubQA"));
            instanceDict.Add("SecureVideoMirthQA",Environment.GetEnvironmentVariable("SecureVideoMirthQA"));
            instanceDict.Add("SecureVideoCatchAll",Environment.GetEnvironmentVariable("SecureVideoCatchAll"));
            instanceDict.Add("SecureVideoHubA01",Environment.GetEnvironmentVariable("SecureVideoHubA01"));
            instanceDict.Add("SecureVideoMirthProd",Environment.GetEnvironmentVariable("SecureVideoMirthProd"));
            instanceDict.Keys.ToList().ForEach(async s=>Console.WriteLine(s));
            instanceDict.Values.ToList().ForEach(async s=>Console.WriteLine(s));
            int imageAgeinDays = int.Parse(Environment.GetEnvironmentVariable("MaxImageAgeInDays"));
            Console.WriteLine(imageAgeinDays);

            DateTime historicImageDate = DateTime.UtcNow.AddDays(-imageAgeinDays);
        
            //Image names must start with SecureVideo
            List<Filter>  filter= new List<Filter>(){ new Filter
            {
                Name = "name",
                Values = new List<string> {"SecureVideo*" },
            },
            };

            DescribeImagesRequest request = new DescribeImagesRequest(){ Filters = filter, 
                                                                        Owners = new List<string>(){Environment.GetEnvironmentVariable("AccountId")}
                                                                        ,MaxResults = 50};

            var response = await _amazonEC2.DescribeImagesAsync(request);
            Console.WriteLine(response?.Images?.Count);
            List<string> instancesUpToDate = new List<string>();
            if (response?.Images?.Count > 0)
            {
                foreach(Image img in response.Images)
                {
                    if (DateTime.Parse(img.CreationDate) >= historicImageDate)
                    {
                        Console.WriteLine($"Image {img.Name} for {img.SourceInstanceId} was created  {img.CreationDate} less than {imageAgeinDays} days ago. Skipping creation");
                        instancesUpToDate.Add(img.SourceInstanceId);
                    }               
                    else
                    {
                        Console.WriteLine($"Image {img.Name} for {img.SourceInstanceId} was created {img.CreationDate}  more than {imageAgeinDays} days ago.");
                    }       
                }   
            }
            
            foreach(KeyValuePair<string,string> kvp in instanceDict)
            {
                if (string.IsNullOrWhiteSpace(kvp.Value))
                {
                    Console.WriteLine($"{kvp.Key} image name does not have an instance ID defined. Skipping...");
                    continue;
                }
                    
                string instanceId = kvp.Value;
                string imageName = kvp.Key;
                Console.WriteLine($"Checking image creation for {imageName}...");
                if (!instancesUpToDate.Contains(instanceId))
                {
                    Console.WriteLine($"{imageName} for {instanceId} is more than {imageAgeinDays} old. Proceeding with image creation..");
                    string name = string.Format("{0}-{1}",imageName, DateTime.UtcNow.Date.ToString("yyyy-MM-dd"));
                    CreateImageRequest request1 = new CreateImageRequest(){ InstanceId=instanceId,
                                                                        Name= name,
                                                                        NoReboot = true, 
                                                                        };
                    // try
                    // {
                    //     var response = await _amazonEC2.CreateImageAsync(request1);
                    //     Console.WriteLine($" Response for image ceation for instance id {instanceId} is {response.HttpStatusCode}");
                    //     Console.WriteLine($"Initiated image creation for {instanceId} with name {name} and Image Id: {response.ImageId}");
                    // }
                    // catch (Exception ex)
                    // {
                    //     Console.WriteLine($"exception received. {ex.Message}");
                    // }  
                }
                else
                {
                    Console.WriteLine($"Looks like Instance ID {instanceId} for server {imageName} has been backed within the last {imageAgeinDays} days.");
                }
            
            }
            return;
        }
    }
}
