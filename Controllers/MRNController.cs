using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Production MRN (Modules/Production/MRN.html): raw material requisitioned for a part being produced.
    /// MRNType 'PROD'. Every endpoint lives in MRNControllerBase.
    /// </summary>
    [Route("api/[controller]")]
    public class MRNController : MRNControllerBase
    {
        public MRNController(EMSContext context) : base(context) { }

        protected override string MRNType => "PROD";
    }
}
