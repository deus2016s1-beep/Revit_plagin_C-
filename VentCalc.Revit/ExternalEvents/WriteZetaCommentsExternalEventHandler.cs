using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VentCalc.Core.Models;
using VentCalc.Revit.Services;
using VentCalc.UI.ViewModels;

namespace VentCalc.Revit.ExternalEvents
{
    public sealed class WriteZetaCommentsExternalEventHandler : IExternalEventHandler
    {
        private readonly RevitVentCalcCenterDataLoader dataLoader;
        private readonly string launchLogPath;
        private readonly Action? focusWindow;
        private ExternalEvent? externalEvent;
        private VentCalcCenterViewModel? pendingViewModel;
        private List<LocalResistanceCalculationInfo> pendingRows = new List<LocalResistanceCalculationInfo>();

        public WriteZetaCommentsExternalEventHandler(RevitVentCalcCenterDataLoader dataLoader, string launchLogPath, Action? focusWindow = null)
        {
            this.dataLoader = dataLoader;
            this.launchLogPath = launchLogPath;
            this.focusWindow = focusWindow;
        }

        public void Initialize(ExternalEvent createdExternalEvent)
        {
            externalEvent = createdExternalEvent;
        }

        public void Request(VentCalcCenterViewModel viewModel, IReadOnlyList<LocalResistanceCalculationInfo> rows)
        {
            pendingViewModel = viewModel;
            pendingRows = rows.Where(row => row.EffectiveZeta > 0 || row.ManualZeta.HasValue || row.AutoZeta > 0).ToList();
            ErrorReporter.WriteTrace(launchLogPath, $"WriteZetaComments requested: {pendingRows.Count} rows");
            externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            VentCalcCenterViewModel? viewModel = pendingViewModel;
            if (viewModel == null)
            {
                return;
            }

            var writtenIds = new List<long>();
            var warnings = new List<string>();
            try
            {
                UIDocument? uiDocument = app.ActiveUIDocument;
                if (uiDocument == null)
                {
                    Complete(viewModel, writtenIds, "Откройте документ Revit перед записью ζ.");
                    return;
                }

                if (pendingRows.Count == 0)
                {
                    Complete(viewModel, writtenIds, "Выберите строки МС с ζ для записи.");
                    return;
                }

                using (var transaction = new Transaction(uiDocument.Document, "VentCalc: записать ζ в комментарии"))
                {
                    transaction.Start();
                    foreach (LocalResistanceCalculationInfo row in pendingRows)
                    {
                        Element? element = uiDocument.Document.GetElement(new ElementId(row.ElementId));
                        if (element == null)
                        {
                            warnings.Add($"ElementId {row.ElementId}: элемент не найден.");
                            continue;
                        }

                        Parameter? comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                            ?? element.LookupParameter("Комментарии")
                            ?? element.LookupParameter("Comments");
                        if (comments == null || comments.IsReadOnly || comments.StorageType != StorageType.String)
                        {
                            warnings.Add($"ElementId {row.ElementId}: параметр Комментарии недоступен для записи.");
                            continue;
                        }

                        double zeta = row.ManualZeta ?? row.EffectiveZeta;
                        string oldComment = comments.AsString() ?? string.Empty;
                        comments.Set(UpdateZetaComment(oldComment, zeta));
                        writtenIds.Add(row.ElementId);
                        row.WasWrittenToRevitComment = true;
                    }

                    transaction.Commit();
                }

                if (writtenIds.Count > 0 && viewModel.LastLoadedElementId.HasValue)
                {
                    VentCalcCenterData data = dataLoader.Load(uiDocument, viewModel.Settings.ToAerodynamicSettings(), new ElementId(viewModel.LastLoadedElementId.Value));
                    InvokeOnUiThread(viewModel, () => viewModel.CompleteLoad(data));
                }

                string message = writtenIds.Count == 0
                    ? $"ζ не записан. {string.Join("; ", warnings)}"
                    : $"ζ записан в комментарии для {writtenIds.Count} элементов. {string.Join("; ", warnings)}";
                Complete(viewModel, writtenIds, message.Trim());
            }
            catch (Exception exception)
            {
                ErrorReporter.Report(app, "Ошибка записи ζ в комментарии", exception, launchLogPath);
                InvokeOnUiThread(viewModel, () => viewModel.FailLoad(exception));
            }
            finally
            {
                focusWindow?.Invoke();
            }
        }

        public string GetName()
        {
            return "VentCalc Write Zeta Comments";
        }

        private static string UpdateZetaComment(string existingComment, double zeta)
        {
            string zetaText = zeta.ToString("0.###", CultureInfo.InvariantCulture);
            const string pattern = @"(?:ζ|zeta|z)\s*=\s*[-+]?\d+(?:[\.,]\d+)?";
            if (Regex.IsMatch(existingComment, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return Regex.Replace(existingComment, pattern, $"z={zetaText}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            return string.IsNullOrWhiteSpace(existingComment)
                ? $"z={zetaText}"
                : $"{existingComment.TrimEnd()} z={zetaText}";
        }

        private static void Complete(VentCalcCenterViewModel viewModel, IReadOnlyCollection<long> writtenIds, string message)
        {
            InvokeOnUiThread(viewModel, () => viewModel.CompleteZetaCommentWrite(writtenIds, message));
        }

        private static void InvokeOnUiThread(VentCalcCenterViewModel viewModel, Action action)
        {
            System.Windows.Application? application = System.Windows.Application.Current;
            if (application?.Dispatcher != null && !application.Dispatcher.CheckAccess())
            {
                application.Dispatcher.Invoke(action);
                return;
            }

            action();
        }
    }
}
