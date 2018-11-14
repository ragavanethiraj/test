using System;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;

namespace helloworld
{
    class Program
    {
        public static async Task RecognizeSpeechAsync()
        {
            // Creates an instance of a speech config with specified subscription key and service region.
            // Replace with your own subscription key and service region (e.g., "westus").
            var config = SpeechConfig.FromSubscription(" 2d204772e91041ada26235dab711c0e0", "westcentralus");

            // Creates a speech recognizer.
            using (var recognizer = new SpeechRecognizer(config))
            {
                Console.WriteLine("Say something...");

                // Performs recognition. RecognizeOnceAsync() returns when the first utterance has been recognized,
                // so it is suitable only for single shot recognition like command or query. For long-running
                // recognition, use StartContinuousRecognitionAsync() instead.
                var result = await recognizer.RecognizeOnceAsync();

                // Checks result.
                if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    Console.WriteLine($"We recognized: {result.Text}");
                }
                else if (result.Reason == ResultReason.NoMatch)
                {
                    Console.WriteLine($"NOMATCH: Speech could not be recognized.");
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = CancellationDetails.FromResult(result);
                    Console.WriteLine($"CANCELED: Reason={cancellation.Reason}");

                    if (cancellation.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"CANCELED: ErrorCode={cancellation.ErrorCode}");
                        Console.WriteLine($"CANCELED: ErrorDetails={cancellation.ErrorDetails}");
                        Console.WriteLine($"CANCELED: Did you update the subscription info?");
                    }
                }
            }
        }

        static void Main()
        {
            RecognizeSpeechAsync().Wait();
            Console.WriteLine("Please press a key to continue.");
            Console.ReadLine();
        }



        #region test 
        public async Task<IHttpActionResult> UploadImage(string fileName = "")
        {
            //Use a GUID in case the fileName is not specified
            if (fileName == "")
            {
                fileName = Guid.NewGuid().ToString();
            }
            //Check if submitted content is of MIME Multi Part Content with Form-data in it?
            if (!Request.Content.IsMimeMultipartContent("form-data"))
            {
                return BadRequest("Could not find file to upload");
            }

            //Read the content in a InMemory Muli-Part Form Data format
            var provider = await Request.Content.ReadAsMultipartAsync(new InMemoryMultipartFormDataStreamProvider());

            //Get the first file
            var files = provider.Files;
            var uploadedFile = files[0];

            //Extract the file extention
            var extension = ExtractExtension(uploadedFile);
            //Get the file's content type
            var contentType = uploadedFile.Headers.ContentType.ToString();

            //create the full name of the image with the fileName and extension
            var imageName = string.Concat(fileName, extension);

            //Initialise Blob and FaceApi connections
            var storageAccount = CloudStorageAccount.Parse(ConfigurationManager.AppSettings["StorageConnectionString"]); //Azure storage account connection
            var _faceServiceClient = new FaceServiceClient(ConfigurationManager.AppSettings["FaceAPIKey"]);  //FaceApi connection
            var blobClient = storageAccount.CreateCloudBlobClient();
            var anonContainer = blobClient.GetContainerReference("powerappimages"); //camera control images
            var d365Container = blobClient.GetContainerReference("entityimages"); //dynamics 365 contact images
            var contactId = Guid.Empty;
            double confidence = 0;
            Entity crmContact = null;

            var blockBlob = anonContainer.GetBlockBlobReference(imageName);
            blockBlob.Properties.ContentType = contentType;

            //Upload anonymous image from camera control to powerappimages blob
            using (var fileStream = await uploadedFile.ReadAsStreamAsync()) //as Stream is IDisposable
            {
                blockBlob.UploadFromStream(fileStream);
            }

            //Detect faces in the uploaded anonymous image
            Face[] anonymousfaces = await _faceServiceClient.DetectAsync(blockBlob.Uri.ToString(), returnFaceId: true, returnFaceLandmarks: true);

            //Iterate stored contact entity images and verify the identity
            foreach (IListBlobItem item in d365Container.ListBlobs(null, true))
            {
                CloudBlockBlob blob = (CloudBlockBlob)item;
                Face[] contactfaces = await _faceServiceClient.DetectAsync(blob.Uri.ToString(), returnFaceId: true, returnFaceLandmarks: true);
                VerifyResult result = await _faceServiceClient.VerifyAsync(anonymousfaces[0].FaceId, contactfaces[0].FaceId);
                if (result.IsIdentical)
                {
                    //Face identified. Retrieve associated contact
                    MatchCollection mc = Regex.Matches(blob.Uri.ToString(),
                        @"([a-z0-9]{8}[-][a-z0-9]{4}[-][a-z0-9]{4}[-][a-z0-9]{4}[-][a-z0-9]{12})"); //strip contact id from image filename
                    contactId = Guid.Parse(mc[0].ToString());
                    confidence = Math.Round((result.Confidence * 100), 2);
                    crmContact = GetContact(contactId);
                    break;
                }
            }
            var fileInfo = new UploadedFileInfo
            {
                FileName = fileName,
                FileExtension = extension,
                ContentType = contentType,
                FileURL = blockBlob.Uri.ToString(),
                ContactId = contactId.ToString(),
                Confidence = confidence.ToString(),
                FirstName = crmContact?.GetAttributeValue<string>("firstname"),
                LastName = crmContact?.GetAttributeValue<string>("lastname"),
                StudentID = crmContact?.GetAttributeValue<string>("sms_studentid"),
            };
            return Ok(fileInfo);

        }

        The GetContact method:

        public static Entity GetContact(Guid id)
        {
            using (var _crmClient = new CrmServiceClient(ConfigurationManager.AppSettings["CRMConnectionString"]))
            {
                ColumnSet cols = new ColumnSet(new string[] { "firstname", "lastname", "sms_studentid" });
                var result = _crmClient.Retrieve("contact", id, cols);
                return result;
            }
        }
        #endregion
    }
}