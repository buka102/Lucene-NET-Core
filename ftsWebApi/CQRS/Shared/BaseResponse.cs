using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ftsWebApi.CQRS.Shared
{
    public class BaseResponse
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; }

        /// <summary>
        /// A helper method to provide JsonResponse with HttpStatusCode based on IsValid property value
        /// </summary>
        /// <param name="overrideStatusCode"></param>
        /// <returns></returns>
        public Microsoft.AspNetCore.Mvc.JsonResult JsonResult(System.Net.HttpStatusCode? overrideStatusCode = null)
        {
            var jsonResult = new Microsoft.AspNetCore.Mvc.JsonResult(this);
            if (overrideStatusCode.HasValue)
            {
                jsonResult.StatusCode = (int)(overrideStatusCode.Value);
            }
            else
            {
                jsonResult.StatusCode = (int)(this.IsValid ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.BadRequest);

            }
            return jsonResult;
        }

    }
}
