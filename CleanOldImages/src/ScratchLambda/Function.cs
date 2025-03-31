using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Amazon.Lambda.Core;
using System.Globalization;
using Amazon.EC2;
using Amazon.EC2.Model;

using Amazon.Lambda.Core;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ScratchLambda
{

public class CleanImagesFunction
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
            (event1.Time.Hour < 11 || event1.Time.Hour > 13) ||
            event1.Resources[0]!="arn:aws:events:us-west-2:822843877307:rule/CleanOldImages")
            {
                throw new Exception("Unexpected event encountered. QUitting processing");
                // System.Console.WriteLine("Unexpected event encountered. QUitting processing.");
                // return;
            }
        Dictionary<string,string> _ec2InstanceIdsForBackup = new Dictionary<string,string>();
        _ec2InstanceIdsForBackup.Add("SecureVideoHubQA",Environment.GetEnvironmentVariable("SecureVideoHubQA"));
        _ec2InstanceIdsForBackup.Add("SecureVideoMirthQA",Environment.GetEnvironmentVariable("SecureVideoMirthQA"));
        _ec2InstanceIdsForBackup.Add("SecureVideoCatchAll",Environment.GetEnvironmentVariable("SecureVideoCatchAll"));
        _ec2InstanceIdsForBackup.Add("SecureVideoHubA01",Environment.GetEnvironmentVariable("SecureVideoHubA01"));
        _ec2InstanceIdsForBackup.Add("SecureVideoMirthProd",Environment.GetEnvironmentVariable("SecureVideoMirthProd"));
        // _ec2InstanceIdsForBackup.Keys.ToList().ForEach(async s=>Console.WriteLine(s));
        // _ec2InstanceIdsForBackup.Values.ToList().ForEach(async s=>Console.WriteLine(s));

        List<Filter>  filter= new List<Filter>(){ new Filter
        {
            Name = "name",
            Values = new List<string> {"SecureVideo*" },
        },
        };
        DescribeImagesRequest request = new DescribeImagesRequest(){ Filters = filter, 
                                                                    Owners = new List<string>(){Environment.GetEnvironmentVariable("AccountId")}
                                                                    ,MaxResults = 100};

        var response = await _amazonEC2.DescribeImagesAsync(request);
        Console.WriteLine($"Total images found starting with SecureVideo {response?.Images?.Count}");

        List<string> instancesUpToDate = new List<string>();
        //var tasks = new List<Task<DeregisterImageResponse>>();

        if (response?.Images?.Count > 0)
        {
            _ec2InstanceIdsForBackup.Keys.ToList().ForEach(async instanceName=>{
                var images= response.Images.Where(img=>img.Name.StartsWith(instanceName));
                if (images.Count() >1)
                {
                    Console.WriteLine($"Multiple images {images.Count()} found for server with image name {instanceName}. Deleting all but latest.");
                    Image[] outDatedImages = images.OrderBy(i=>i.CreationDate).ToArray();
                    for (int i = 0; i < outDatedImages.Length-1; i++)
                    {
                        if (outDatedImages[i].SourceInstanceId != _ec2InstanceIdsForBackup[instanceName])
                        {
                            Console.WriteLine($"For Image starting with name {instanceName} Instance id expected {_ec2InstanceIdsForBackup[instanceName]} did not match instance Id from AWS {outDatedImages[i].SourceInstanceId}   ");
                            continue;
                        }
                        Console.WriteLine($"Initiating deregister for image name {outDatedImages[i].Name} and ID {outDatedImages[i].ImageId}");
                        DeregisterImageRequest deregReq = new DeregisterImageRequest(){ ImageId = outDatedImages[i].ImageId};
                        try
                        {
                            Console.WriteLine($"Inside try block to dregister image");
                            var response1 = _amazonEC2.DeregisterImageAsync(deregReq).Result;
                            
                            Console.WriteLine($" Response for image deregister for image name {outDatedImages[i].Name} is {response1.HttpStatusCode}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"exception received. {ex.Message}");
                        }
                    }   

                }
                else
                {
                    Console.WriteLine($"{images.Count()} image(s) found for server with image name starting with {instanceName}.");
    
                }
            });              
        }       
    }
}
}