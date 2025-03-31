To get started with this solution, install the Sam Cli 
https://docs.aws.amazon.com/serverless-application-model/latest/developerguide/install-sam-cli.html
The BackupServer and CleanOldImages lambdas are used to backup the Secure Video Server AMIs  and clean up old AMIs (so we only have the latest image).
The server images being backed up are defined in the configuration of each lamnbda.
Both these lambdas  are written in C# and deployed/built/created using the sam cli. Thee backup server images is rriggered with a scheduled event that fires Sundays at 3 AM ET.
The clean images is triggered a few hours later.
Typing sam --help will give you the list of all commands needed.
