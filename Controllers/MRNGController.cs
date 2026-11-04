using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// General / Extra MRN (Modules/Production/MRNG.html): any item, no part-to-produce.
    /// MRNType 'GEN'. Every endpoint lives in MRNControllerBase.
    /// </summary>
    [Route("api/[controller]")]
    public class MRNGController : MRNControllerBase
    {
        public MRNGController(EMSContext context) : base(context) { }

        protected override string MRNType => "GEN";
    }
}
