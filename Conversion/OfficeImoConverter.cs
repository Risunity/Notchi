using System;
using System.IO;
using System.Threading;
using OfficeIMO;
using OfficeIMO.Pdf;
using OfficeIMO.Word;
using OfficeIMO.Word.LegacyDoc;
using OfficeIMO.Word.Pdf;

namespace WindowsNotch.Conversion
{
    public static class OfficeImoConverter
    {
        public static ConversionResultFile Convert(
            ConversionJobFile job,
            CancellationToken cancellationToken)
        {
            try
            {
                return job.Target switch
                {
                    nameof(ConversionTarget.Pdf) => ConversionFormats.IsLegacyDoc(job.InputPath)
                        ? LegacyDocToPdf(job, cancellationToken)
                        : DocxToPdf(job, cancellationToken),
                    nameof(ConversionTarget.Docx) => PdfToDocx(job, cancellationToken),
                    _ => Failure("Unsupported conversion target.")
                };
            }
            catch (Exception exception)
            {
                CrashLogger.Log("OfficeImoConverter.Convert", exception);
                return Failure(exception.Message);
            }
        }

        private static ConversionResultFile DocxToPdf(
            ConversionJobFile job,
            CancellationToken cancellationToken)
        {
            string? sanitizedPath = null;

            try
            {
                string sanitizedDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "Notchi",
                    "convert");
                Directory.CreateDirectory(sanitizedDirectory);

                string candidate = Path.Combine(
                    sanitizedDirectory,
                    Guid.NewGuid().ToString("N") + ".docx");

                string loadPath = job.InputPath;

                if (DocxSanitizer.TryCreateSanitizedCopy(
                        job.InputPath,
                        candidate,
                        out bool changed))
                {
                    if (changed)
                    {
                        sanitizedPath = candidate;
                        loadPath = candidate;
                    }
                    else
                    {
                        TryDelete(candidate);
                    }
                }

                using WordDocument word = WordDocument.Load(loadPath);

                PdfSaveResult result = word.SaveAsPdfResult(
                    job.TempOutputPath,
                    new WordToPdfOptions(),
                    cancellationToken);

                if (!result.Succeeded)
                {
                    if (result.Exception != null)
                    {
                        CrashLogger.Log("OfficeImoConverter.DocxToPdf", result.Exception);
                    }

                    return Failure(
                        result.Exception?.Message ?? "Word to PDF conversion failed.");
                }

                return new ConversionResultFile(
                    true,
                    job.TempOutputPath,
                    null);
            }
            finally
            {
                if (sanitizedPath != null)
                {
                    TryDelete(sanitizedPath);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static ConversionResultFile LegacyDocToPdf(
            ConversionJobFile job,
            CancellationToken cancellationToken)
        {
            using FileStream input = File.OpenRead(job.InputPath);

            PdfDocumentConversionResult conversion =
                LegacyDocPdfConverter.ToPdfDocumentResult(
                    input,
                    new WordToPdfOptions(),
                    new LegacyDocImportOptions
                    {
                        ReportUnsupportedContent = true
                    },
                    OfficeConversionLossPolicy.Allow,
                    cancellationToken);

            PdfSaveResult save = conversion.SaveResult(
                job.TempOutputPath,
                cancellationToken);

            if (!save.Succeeded)
            {
                return Failure(
                    save.Exception?.Message ?? "Legacy DOC to PDF conversion failed.");
            }

            return new ConversionResultFile(
                true,
                job.TempOutputPath,
                null);
        }

        private static ConversionResultFile PdfToDocx(
            ConversionJobFile job,
            CancellationToken cancellationToken)
        {
            PdfDocument pdf = PdfDocument.Load(
                job.InputPath,
                PdfLoadOptions.Default,
                cancellationToken);

            PdfDocumentReadResult read = pdf.Read(
                new PdfReadOptions
                {
                    IncludeImages = true,
                    IncludeFormWidgets = true
                },
                cancellationToken);

            if (string.IsNullOrWhiteSpace(read.Text))
            {
                return Failure("Scanned PDF has no text layer.");
            }

            var import = read.ToWordDocumentResult(
                new PdfToWordOptions(),
                cancellationToken);

            using WordDocument word = import.Value;
            word.Save(job.TempOutputPath);

            return new ConversionResultFile(
                true,
                job.TempOutputPath,
                null);
        }

        private static ConversionResultFile Failure(string error)
        {
            return new ConversionResultFile(false, null, error);
        }
    }
}
