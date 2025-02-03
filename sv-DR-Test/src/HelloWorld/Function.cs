using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.EC2;
using Amazon.EC2.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace HelloWorld
{

    public class Function
    {

        private static readonly HttpClient m_client = new HttpClient(){
                        Timeout= new TimeSpan(0,1,0)};
        
        static  IAmazonEC2 _amazonEC2 = new AmazonEC2Client();

        // private static async Task<string> GetCallingIP()
        // {
        //     client.DefaultRequestHeaders.Accept.Clear();
        //     client.DefaultRequestHeaders.Add("User-Agent", "AWS Lambda .Net Client");

        //     var msg = await client.GetStringAsync("http://checkip.amazonaws.com/").ConfigureAwait(continueOnCapturedContext:false);

        //     return msg.Replace("\n","");
        // }


        public async Task FunctionHandler(Amazon.Lambda.CloudWatchEvents.ScheduledEvents.ScheduledEvent event1, ILambdaContext context)
        {
            //System.Console.WriteLine($"gotchaaas {event1.Region} {event1.DetailType}");
            
            System.Console.WriteLine($"gotchaaas {Newtonsoft.Json.JsonConvert.SerializeObject(event1)}");
            
            System.Console.WriteLine($"{Environment.GetEnvironmentVariable("BackupDRServerTag")}");
            string backupDRServerTag = Environment.GetEnvironmentVariable("BackupDRServerTag");
            string prodInstanceId =Environment.GetEnvironmentVariable("ProdInstanceId");
            string pingAddress = Environment.GetEnvironmentVariable("PingAddress");
            string drLaunchTemplateID = Environment.GetEnvironmentVariable("DrLaunchTemplateID");
            try
            {
                if (IsDRInProgress(backupDRServerTag))
                {
                    Console.WriteLine("DR already in progress");
                    return;
                }
                
                if (IsCurrentProdInstanceReachable(prodInstanceId))
                {
                    Console.WriteLine($"Prod Instance ID is raechabale and running");
                    Console.WriteLine("Pinging website health check to verify site..");
                    if (PingWebsite(pingAddress))
                    {
                        Console.WriteLine($"Site {pingAddress} is reachable");
                        return;
                    }

                    Console.WriteLine("Site not reachable. Moving to next phase of DR...");

                    string instanceId = LaunchDrInstance(drLaunchTemplateID);
                    if (string.IsNullOrWhiteSpace(instanceId))
                    {
                        Console.WriteLine("DR server was not started");
                        return;
                    }
                    
                    int i=0;
                    while(!IsDrServerRunning(instanceId) && i++ < 40)
                    {
                        Console.WriteLine($"Checking if instance iD {instanceId} is running.. attempt {i}");
                        await Task.Delay(10000);
                        //Thread.Sleep(10000);
                    }

                    if (i >=30)
                    {
                        Console.WriteLine($"Unable to detect if instance was started or not in the given time");
                    }
                }
            }
            catch (AmazonEC2Exception ec2Exception)
            {
                if (ec2Exception.ErrorCode == "InvalidParameterValue")
                {
                    Console.WriteLine(
                        $"Invalid parameter value for filtering instances.");
                }        
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in function handler: {ex.Message}");
                
            }
         
         return;
            
        }

        private static string LaunchDrInstance(string launchtemplateID)
        {
            RunInstancesRequest request = new RunInstancesRequest();
            request.MaxCount=1;
            request.MinCount=1;
        
            request.LaunchTemplate = new LaunchTemplateSpecification(){LaunchTemplateId = launchtemplateID};
            
            RunInstancesResponse response = _amazonEC2.RunInstancesAsync(request).Result;
            if (response.HttpStatusCode == System.Net.HttpStatusCode.OK) 
            {
                Instance instance = response.Reservation.Instances[0];
                Console.WriteLine(@$"Succefully initiated launch of DR instanced with ID {instance.InstanceId}
                    and  private IP {instance.PrivateIpAddress} and public IP of {instance.PublicIpAddress} and Instance type of {instance.InstanceType}
                    and tag name of {instance.Tags[2].Value}");
                return instance.InstanceId;
            }
            else
            {
                Console.WriteLine(@$"Received status of {response.HttpStatusCode}");
                return string.Empty;
            }

        }

   
        
        private static bool PingWebsite(string siteName)
        {
            int i=0;
            while (i < 3)
            {
                
                try{
                    Console.WriteLine($"Attempt {i+1} to ping...");
                    var response =  m_client.GetAsync(siteName).Result;
                    if(response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        Console.WriteLine($"Website reachable! Reponse from site  containt SecureVideo - Log in is {response.Content.ReadAsStringAsync().Result.Contains("SecureVideo - Log in")}");
                        break;
                    }
                    Console.WriteLine($"Response code is {response.StatusCode}");    
                    
                }
                catch(TimeoutException ex1)
                {
                    Console.WriteLine($"Timeout exception received. {ex1.Message}");
                }
                catch (Exception ex)
                {
                        Console.WriteLine($"exception received. {ex.Message}");
                }   
                finally{
                    i++;
                } 
            }

            if (i ==3)
            {
                Console.WriteLine($"Tried pinging {i} times and failed. Possible DR scenario");
                return false;
            }
            else{
                return true;
            }
            
        }
        private static bool IsCurrentProdInstanceReachable(string prodInstanceId)
        {
            var filtersStatus= new List<Filter>
                {
                    new Filter
                    {
                        Name = "instance-status.reachability",
                        Values = new List<string> { "passed"},
                    },
                    new Filter
                    {
                        Name = "system-status.reachability",
                        Values = new List<string> { "passed"},
                    }, new Filter
                    {
                        Name = "instance-state-name",
                        Values = new List<string> { "running"},
                    },
                };
                //i-023051b34cdeda7b0 is HUBA. Need a better way to identify prod instances at some point.
                var request1 = new DescribeInstanceStatusRequest(){InstanceIds = new List<string>(){ prodInstanceId } 
                                                                ,Filters=filtersStatus};
                var response1 = _amazonEC2.DescribeInstanceStatusAsync(request1).Result;
                if(response1?.InstanceStatuses?.Count() >=1)
                {
                    response1.InstanceStatuses.ForEach(x=>{
                    Console.WriteLine(@$"Instance ID {x.InstanceId} has instance state {x.InstanceState.Name}
                                    and instance status property {x.Status.Details.ToArray()[0].Name} value  {x.Status.Details.ToArray()[0].Status}
                                    with system status {x.SystemStatus.Details.ToArray()[0].Status}");
                                
                    });
                    return true;
                }
                else
                {
                    return false;
                }
        }

        private static  bool IsDrServerRunning(String drInstanceId)
        {
            try{
                var filtersStatus= new List<Filter> 
                {
                    new Filter
                    {
                        Name = "instance-state-name",
                        Values = new List<string> { "running"},
                    }
                };
                var request1 = new DescribeInstanceStatusRequest(){InstanceIds = new List<string>(){ drInstanceId } 
                                                                ,Filters=filtersStatus};
                var response1 = _amazonEC2.DescribeInstanceStatusAsync(request1).Result;
                if(response1?.InstanceStatuses?.Count ==1)
                {
                    Console.WriteLine($"Server with instance ID {drInstanceId} was found to be running");
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Failed to get instance status for {drInstanceId} with error {ex.Message}");
                return false;
            }
        }
        private static  bool IsDRInProgress(string drServerTagname)
        {
            //Check if DR is in progress by verifying if HUBB is running.
                var drServerStatus = new List<Filter>
                {
                    new Filter
                    {
                        Name="tag:Name",
                        Values = new List<string>{ drServerTagname}
                    }
                };
                var drServerStatusRequest = new DescribeInstancesRequest() {Filters = drServerStatus};
                DescribeInstancesResponse drResp = _amazonEC2.DescribeInstancesAsync(drServerStatusRequest).Result;
                if (drResp.Reservations?.Count >0)
                {
                    
                    Console.WriteLine($"DR is in progress since HUBB01 server found in state {drResp.Reservations[0].Instances[0].State.Name}");
                    return true;
                }
                else
                    return false;
        }
        // public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest apigProxyEvent, ILambdaContext context)
        // {

        //     var location = await GetCallingIP();
        //     var body = new Dictionary<string, string>
        //     {
        //         { "message", "hello world" },
        //         { "location", location }
        //     };

        //     return new APIGatewayProxyResponse
        //     {
        //         Body = JsonSerializer.Serialize(body),
        //         StatusCode = 200,
        //         Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        //     };
        // }
    }
}
