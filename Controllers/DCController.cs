using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Delivery Challan — RGPMst / RGPDtl / RGPTerms rows with RGPType 'DC',
    /// as in the AquaFlow app. Every endpoint (Mst/*, Dtl/*, Terms/*, incl.
    /// Mst/Approve and Mst/GetNextDocNo) is inherited from
    /// <see cref="RGPOutwardControllerBase"/>, which filters and forces the
    /// type, so the RGP list never shows a challan and vice versa.
    ///
    /// A challan is non-returnable: SP_RGPInwMst refuses one as an inward
    /// target, and RGP Inward's source list only offers RGPType 'RGP'.
    /// Numbering is its own: DC and RGP keep separate DocNo sequences inside
    /// a series + year.
    /// </summary>
    [Route("api/[controller]")]
    public class DCController : RGPOutwardControllerBase
    {
        public DCController(EMSContext context) : base(context) { }

        protected override string RGPType => "DC";
        protected override string DocLabel => "Delivery challan";
    }
}
