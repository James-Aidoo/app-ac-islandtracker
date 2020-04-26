using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TurnipTracker.Functions.Helpers;
using Microsoft.WindowsAzure.Storage.Table;
using TurnipTracker.Shared;
using System.Linq;
using System.Web.Http;

namespace TurnipTracker.Functions
{
    public static class UpdateProfile
    {
        [FunctionName(nameof(UpdateProfile))]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = null)] HttpRequest req,
            [Table("User")] CloudTable cloudTable,
            ILogger log)
        {
            log.LogInformation($"C# HTTP trigger {nameof(UpdateProfile)} function processed a request.");


            var privateKey = Utils.ParseToken(req);
            if (privateKey == null)
                return new UnauthorizedResult();

            User user = null;

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                user = JsonConvert.DeserializeObject<User>(requestBody);
            }
            catch (Exception ex)
            {
                log.LogInformation("Unable to deserialize user: " + ex.Message);

            }

            if (user == null ||
                string.IsNullOrWhiteSpace(user.PublicKey) ||
                string.IsNullOrWhiteSpace(user.Name) ||
                string.IsNullOrWhiteSpace(user.IslandName) ||
                string.IsNullOrWhiteSpace(user.TimeZone))
            {
                return new BadRequestResult();
            }

            UserEntity userEntity = null;
            try
            {
                userEntity = await Utils.FindUserEntity(cloudTable, privateKey, user.PublicKey);
            }
            catch (Exception ex)
            {
                //user does not exist? correct error?
                return new InternalServerErrorResult();
            }

            if (userEntity == null)
                return new BadRequestResult();


            userEntity.Name = user.Name;
            userEntity.IslandName = user.IslandName;
            userEntity.Fruit = user.Fruit;
            userEntity.TimeZone = user.TimeZone;
            userEntity.Status = user.Status ?? string.Empty;

            try
            {
                await Utils.MergeUserEntity(cloudTable, userEntity);
            }
            catch (Exception ex)
            {
                return new InternalServerErrorResult();
            }

            return new OkObjectResult("User Updated");
        }


    }
}
