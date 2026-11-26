/* ============================================================================
   81_SalesInvoiceServerTax.sql  (idempotent)

   GST on a Sales Invoice was worked out only in the browser and the server
   stored whatever it was sent, so a changed request (or a page bug) could
   store the wrong tax. It also chose IGST vs CGST+SGST from the OA's Tax
   Type text, which is "REGULAR" / "URD" in practice, so an inter-state sale
   (customer in another state) was charged CGST + SGST.

   SP_STxnRecalc recomputes one invoice from its stored lines. The API calls
   it after every header save and every line save / delete, so what is stored
   is always the server's figure.

   Per line (the same formula as SalesInvoice.html):
     Basic    = Qty x Rate
     Discount = Basic x DiscP / 100
     P&F      = PnFP as a flat amount (0 on a line with no qty)
     Freight  = FreightRate x Qty
     Taxable  = Basic - Discount + P&F + Freight
     GST %    = the OA line's GSTP (STxnDtl.LinkNo -> OADtl); 0 on an Export
                invoice (zero-rated)
     GST      = Taxable x GST % / 100, split:
                IGST            - inter-state: Export / SEZ, or the customer's
                                  state (first 2 digits of the invoice GSTIN,
                                  else its State Name) differs from the
                                  company's (Company.StateCode / State);
                CGST + SGST     - same state (half each; the odd paisa to SGST).
     Total    = Taxable + GST
   Header: the sums of the lines (a line with GST 0 is exempt value), Round Off
   (home currency only; a typed value within +/-1 is kept, otherwise it is
   worked out to the nearest rupee), Net = lines' total + Round Off, Currency
   Amount = Net x Currency Rate.
   ============================================================================ */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
/* Is a supply inter-state (IGST) rather than intra-state (CGST + SGST)?
   Export / SEZ (or a Tax Type that says IGST / INTER) - always IGST. Else the
   customer's state - the first 2 digits of its GSTIN, else its State Name -
   against the company's (Company.StateCode / GSTIN / State). Unknown -> 0. */
CREATE OR ALTER FUNCTION dbo.FN_IsInterStateSupply
    (@Gstin VARCHAR(30), @StateName VARCHAR(100), @InvType VARCHAR(50), @TaxType VARCHAR(50))
RETURNS BIT
AS
BEGIN
    DECLARE @CompState VARCHAR(10), @CompStateName VARCHAR(100);
    SELECT TOP (1) @CompState = LTRIM(RTRIM(ISNULL(NULLIF(StateCode, ''), LEFT(GSTIN, 2)))),
                   @CompStateName = UPPER(LTRIM(RTRIM(ISNULL(State, ''))))
      FROM dbo.Company WHERE ISNULL(DeleteStatus, 'N') = 'N' ORDER BY MTransNo;
    SET @Gstin = UPPER(LTRIM(RTRIM(ISNULL(@Gstin, ''))));
    SET @StateName = UPPER(LTRIM(RTRIM(ISNULL(@StateName, ''))));
    SET @TaxType = UPPER(LTRIM(RTRIM(ISNULL(@TaxType, ''))));
    DECLARE @CustState VARCHAR(10) = CASE WHEN @Gstin LIKE '[0-9][0-9]%' THEN LEFT(@Gstin, 2) ELSE '' END;
    RETURN CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(@InvType, '')))) = 'EXPORT' OR @TaxType LIKE '%SEZ%' OR @TaxType LIKE '%EXPORT%'
                     OR @TaxType LIKE '%INTER%' OR @TaxType LIKE '%IGST%' THEN 1
                WHEN @CustState <> '' AND ISNULL(@CompState, '') <> '' THEN CASE WHEN @CustState <> RIGHT('0' + @CompState, 2) THEN 1 ELSE 0 END
                WHEN @StateName <> '' AND ISNULL(@CompStateName, '') <> '' THEN CASE WHEN @StateName <> @CompStateName THEN 1 ELSE 0 END
                ELSE 0 END;
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_STxnRecalc
    @MTransNo INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @InvType VARCHAR(50), @TaxType VARCHAR(50), @Gstin VARCHAR(30), @StateName VARCHAR(100),
            @Currency VARCHAR(10), @CurRate NUMERIC(18, 6), @RoundOff NUMERIC(18, 2);
    SELECT @InvType = LTRIM(RTRIM(ISNULL(InvType, ''))), @TaxType = UPPER(LTRIM(RTRIM(ISNULL(TaxType, '')))),
           @Gstin = UPPER(LTRIM(RTRIM(ISNULL(GSTIN, '')))), @StateName = UPPER(LTRIM(RTRIM(ISNULL(StateName, '')))),
           @Currency = UPPER(LTRIM(RTRIM(ISNULL(TxnCurrency, '')))), @CurRate = ISNULL(CurrencyRate, 1),
           @RoundOff = ISNULL(RoundOff, 0)
      FROM dbo.STxnMst WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';
    IF @@ROWCOUNT = 0 RETURN;

    DECLARE @IsExport BIT = CASE WHEN UPPER(@InvType) = 'EXPORT' THEN 1 ELSE 0 END;
    DECLARE @Igst BIT = dbo.FN_IsInterStateSupply(@Gstin, @StateName, @InvType, @TaxType);

    ;WITH l AS (
        SELECT d.TransSlNo,
               CAST(ISNULL(d.Qty, 0) * ISNULL(d.Rate, 0) AS NUMERIC(28, 8))                                   AS Basic,
               CAST(ISNULL(d.Qty, 0) * ISNULL(d.Rate, 0) * ISNULL(d.DiscP, 0) / 100 AS NUMERIC(28, 8))       AS Disc,
               CAST(CASE WHEN ISNULL(d.Qty, 0) > 0 THEN ISNULL(d.PnFP, 0) ELSE 0 END AS NUMERIC(28, 8))       AS PnF,
               CAST(ISNULL(d.FreightRate, 0) * ISNULL(d.Qty, 0) AS NUMERIC(28, 8))                           AS Freight,
               CAST(CASE WHEN @IsExport = 1 THEN 0 ELSE ISNULL(od.GSTP, ISNULL(d.GSTP, 0)) END AS NUMERIC(9, 2)) AS GstP
          FROM dbo.STxnDtl d
          LEFT JOIN dbo.OADtl od ON od.TransSlNo = d.LinkNo
         WHERE d.MTransNo = @MTransNo AND d.DeleteStatus = 'N'),
    t AS (
        SELECT l.*, l.Basic - l.Disc + l.PnF + l.Freight AS Taxable,
               (l.Basic - l.Disc + l.PnF + l.Freight) * l.GstP / 100 AS Gst
          FROM l),
    s AS (
        SELECT t.*, ROUND(t.Gst, 2) AS GstR,
               CASE WHEN @Igst = 1 THEN 0 ELSE ROUND(ROUND(t.Gst, 2) / 2, 2) END AS Cgst
          FROM t)
    UPDATE d
       SET d.BasicValue   = ROUND(s.Basic, 2),
           d.DiscAmt      = ROUND(s.Disc, 2),
           d.PnFAmt       = ROUND(s.PnF, 2),
           d.FreightAmt   = ROUND(s.Freight, 2),
           d.TaxableValue = ROUND(s.Taxable, 2),
           d.GSTP         = s.GstP,
           d.IGSTAmt      = CASE WHEN @Igst = 1 THEN s.GstR ELSE 0 END,
           d.CGSTAmt      = s.Cgst,
           d.SGSTAmt      = CASE WHEN @Igst = 1 THEN 0 ELSE s.GstR - s.Cgst END,
           d.TotalValue   = ROUND(s.Taxable + s.Gst, 2)
      FROM dbo.STxnDtl d
      JOIN s ON s.TransSlNo = d.TransSlNo;

    -- Header from the lines.
    DECLARE @Basic NUMERIC(28, 8), @Disc NUMERIC(28, 8), @PnF NUMERIC(28, 8), @Freight NUMERIC(28, 8),
            @Taxable NUMERIC(28, 8), @Exempt NUMERIC(28, 8), @Total NUMERIC(28, 8),
            @I NUMERIC(18, 2), @C NUMERIC(18, 2), @S NUMERIC(18, 2);
    ;WITH l AS (
        SELECT CAST(ISNULL(d.Qty, 0) * ISNULL(d.Rate, 0) AS NUMERIC(28, 8)) AS Basic,
               CAST(ISNULL(d.Qty, 0) * ISNULL(d.Rate, 0) * ISNULL(d.DiscP, 0) / 100 AS NUMERIC(28, 8)) AS Disc,
               CAST(CASE WHEN ISNULL(d.Qty, 0) > 0 THEN ISNULL(d.PnFP, 0) ELSE 0 END AS NUMERIC(28, 8)) AS PnF,
               CAST(ISNULL(d.FreightRate, 0) * ISNULL(d.Qty, 0) AS NUMERIC(28, 8)) AS Freight,
               ISNULL(d.GSTP, 0) AS GstP, d.IGSTAmt, d.CGSTAmt, d.SGSTAmt
          FROM dbo.STxnDtl d WHERE d.MTransNo = @MTransNo AND d.DeleteStatus = 'N')
    SELECT @Basic = SUM(Basic), @Disc = SUM(Disc), @PnF = SUM(PnF), @Freight = SUM(Freight),
           @Taxable = SUM(CASE WHEN GstP > 0 THEN Basic - Disc + PnF + Freight ELSE 0 END),
           @Exempt  = SUM(CASE WHEN GstP > 0 THEN 0 ELSE Basic - Disc + PnF + Freight END),
           @Total   = SUM((Basic - Disc + PnF + Freight) * (1 + GstP / 100)),
           @I = SUM(ISNULL(IGSTAmt, 0)), @C = SUM(ISNULL(CGSTAmt, 0)), @S = SUM(ISNULL(SGSTAmt, 0))
      FROM l;

    DECLARE @Home BIT = CASE WHEN @Currency IN ('', 'INR') THEN 1 ELSE 0 END;
    SET @RoundOff = CASE WHEN @Home = 0 THEN 0
                         WHEN ABS(@RoundOff) <= 1 AND @RoundOff <> 0 THEN @RoundOff
                         ELSE ROUND(ISNULL(@Total, 0), 0) - ROUND(ISNULL(@Total, 0), 2) END;
    DECLARE @Net NUMERIC(18, 2) = ROUND(ISNULL(@Total, 0) + @RoundOff, 2);

    UPDATE dbo.STxnMst
       SET BasicValue     = ROUND(ISNULL(@Basic, 0), 2),
           DiscountAmt    = ROUND(ISNULL(@Disc, 0), 2),
           PnFAmt         = ROUND(ISNULL(@PnF, 0), 2),
           FreightAmt     = ROUND(ISNULL(@Freight, 0), 2),
           TaxableValue   = ROUND(ISNULL(@Taxable, 0), 2),
           ExemptValue    = ROUND(ISNULL(@Exempt, 0), 2),
           IGSTAmt        = ISNULL(@I, 0),
           CGSTAmt        = ISNULL(@C, 0),
           SGSTAmt        = ISNULL(@S, 0),
           RoundOff       = @RoundOff,
           NetValue       = @Net,
           CurrencyAmount = ROUND(@Net * ISNULL(@CurRate, 1), 2)
     WHERE MTransNo = @MTransNo;
END;
GO
