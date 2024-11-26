var zlib = require('zlib');
var https = require('https');

const doPostRequest = async (data)  => {

  
    return new Promise((resolve, reject) => {
      const options = {
        host: 'hub-qa.securevideo.com',
        path: '/AmazonSESPushNotification/postlogs',
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        }
      };
      
      //create the request object with the callback with the result
      const req = https.request(options, (res) => {
        resolve(JSON.stringify(res.statusCode));
      });
  
      // handle the possible errors
      req.on('error', (e) => {
        //console.log(e);
        reject(e.message);
      });
      
      //console.log(JSON.stringify(data));
      //do the request
      req.write(data);
  
      //finish the request
      req.end();
    });
  };
  
exports.handler = function (input, context) {
    var payload = Buffer.from(input.awslogs.data, 'base64');
    zlib.gunzip(payload, async function(e, result) {
        if (e) { 
            context.fail(e);
        } else {
            result = JSON.parse(result.toString());
			       await doPostRequest(JSON.stringify(result, null, 2))
			      .then(result => console.log(`Status code: ${result}`))
            .catch(err => console.error(`Error doing the request for the event:  ${err}`));
            console.log("Event Data:", JSON.stringify(result, null, 2));
            context.succeed();
        }
    });
};