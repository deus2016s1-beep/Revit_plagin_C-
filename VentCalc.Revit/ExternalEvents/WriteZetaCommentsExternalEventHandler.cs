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
        private ZetaOverrideRequestMode pendingMode = ZetaOverrideRequestMode.SaveOverrides;

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

        public void Request(VentCalcCenterViewModel viewModel, IReadOnlyList<LocalResistanceCalculationInfo> rows, ZetaOverrideRequestMode mode)
        {
            pendingViewModel = viewModel;
            pendingMode = mode;
            pendingRows = mode == ZetaOverrideRequestMode.SaveOverrides
                ? rows
                    .Where(row => row.ManualZeta.HasValue)
                    .GroupBy(row => row.PathDependent ? row.OverrideKey : row.ElementId.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList()
                : rows
                    .GroupBy(row => row.PathDependent ? row.OverrideKey : row.ElementId.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList();
            ErrorReporter.WriteTrace(launchLogPath, $"WriteZetaComments requested: mode={pendingMode}; rows={pendingRows.Count}");
            externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            VentCalcCenterViewModel? viewModel = pendingViewModel;
            if (viewModel == null)
            {
                return;
            }

            var actions = new List<ZetaWriteActionInfo>();
            try
            {
                UIDocument? uiDocument = app.ActiveUIDocument;
                if (uiDocument == null)
                {
                    Complete(viewModel, actions, "Запись ζ: успешно 0, ошибок 1. Откройте документ Revit.");
                    return;
                }

                if (pendingMode == ZetaOverrideRequestMode.SaveProjectCatalog)
                {
                    int saved = SaveProjectCatalog(uiDocument.Document, viewModel);
                    actions.Add(new ZetaWriteActionInfo
                    {
                        Timestamp = DateTime.Now,
                        OverrideStorageType = "ProjectCatalog",
                        VerificationMode = ZetaVerificationMode.ClearProjectCatalogValue,
                        WriteSucceeded = true,
                        VerifiedAfterCommit = true,
                        ActualCommentAfterCommit = $"Project catalog rows={saved}"
                    });
                    ReloadIfPossible(viewModel, uiDocument, actions);
                    Complete(viewModel, actions, $"Каталог сохранён: ролей {saved}, ошибок 0.");
                    return;
                }

                if (pendingMode == ZetaOverrideRequestMode.ResetProjectToAuto)
                {
                    ResetWholeProjectToAuto(uiDocument.Document, actions);
                    ReloadIfPossible(viewModel, uiDocument, actions);
                    Complete(viewModel, actions, $"Возвращён весь проект к Auto: {actions.Count(action => action.WriteSucceeded)}, ошибок: {actions.Count(action => !action.WriteSucceeded)}.");
                    return;
                }

                if (pendingRows.Count == 0)
                {
                    Complete(viewModel, actions, "Запись ζ: успешно 0, ошибок 1. Выберите строки МС.");
                    return;
                }

                using (var transaction = new Transaction(uiDocument.Document, pendingMode == ZetaOverrideRequestMode.ResetToAuto ? "VentCalc: вернуть ζ к Auto" : "VentCalc: записать ζ в комментарии"))
                {
                    transaction.Start();
                    foreach (LocalResistanceCalculationInfo row in pendingRows)
                    {
                        ZetaWriteActionInfo action = CreateBaseAction(row);
                        actions.Add(action);

                        if (pendingMode == ZetaOverrideRequestMode.ResetToAuto)
                        {
                            ResetRowToAuto(uiDocument.Document, row, action);
                            continue;
                        }

                        if (!TryResolveRequestedZeta(row, out double zeta, out string zetaError))
                        {
                            action.ErrorMessage = zetaError;
                            continue;
                        }

                        action.RequestedZeta = zeta;
                        if (row.PathDependent)
                        {
                            SavePathDependentOverride(uiDocument.Document, row, zeta);
                            action.OverrideStorageType = "DataStorage";
                            action.WriteSucceeded = true;
                            action.ErrorMessage = string.Empty;
                            row.WasSavedToVentCalcStorage = true;
                            row.LastWriteError = string.Empty;
                            continue;
                        }

                        Element? element = uiDocument.Document.GetElement(new ElementId(row.ElementId));
                        if (element == null)
                        {
                            action.ErrorMessage = "Элемент не найден.";
                            continue;
                        }

                        Parameter? comments = FindCommentsParameter(element, out string parameterName);
                        action.ParameterFound = comments != null;
                        action.ParameterName = parameterName;
                        action.ParameterIsReadOnly = comments?.IsReadOnly ?? false;
                        action.StorageType = comments?.StorageType.ToString() ?? string.Empty;
                        action.OverrideStorageType = "Comment";
                        if (comments == null)
                        {
                            action.ErrorMessage = "Параметр Комментарии не найден.";
                            continue;
                        }

                        if (comments.IsReadOnly)
                        {
                            action.ErrorMessage = "Параметр Комментарии недоступен для записи.";
                            continue;
                        }

                        if (comments.StorageType != StorageType.String)
                        {
                            action.ErrorMessage = $"Параметр Комментарии имеет тип {comments.StorageType}, ожидался String.";
                            continue;
                        }

                        action.OldComment = comments.AsString() ?? string.Empty;
                        action.NewComment = UpdateZetaComment(action.OldComment, zeta);
                        comments.Set(action.NewComment);
                        string verifyComment = comments.AsString() ?? string.Empty;
                        action.ZetaTokenExistsAfterCommit = ContainsAnyZeta(verifyComment);
                        action.WriteSucceeded = ContainsExpectedZeta(verifyComment, zeta);
                        action.ErrorMessage = action.WriteSucceeded ? string.Empty : "После записи комментарий не содержит ожидаемое z=...";
                        row.WasWrittenToRevitComment = action.WriteSucceeded;
                        row.LastWriteError = action.ErrorMessage;
                    }

                    transaction.Commit();
                }

                VerifyCommittedComments(uiDocument.Document, actions);
                VerifyStoredOverrides(uiDocument.Document, actions);

                ReloadIfPossible(viewModel, uiDocument, actions);

                int successCount = actions.Count(action => action.WriteSucceeded);
                int errorCount = actions.Count - successCount;
                string errors = string.Join("; ", actions.Where(action => !action.WriteSucceeded && !string.IsNullOrWhiteSpace(action.ErrorMessage)).Select(action => $"{action.ElementId}: {action.ErrorMessage}"));
                string message = $"Запись ζ: успешно {successCount}, ошибок {errorCount}." + (string.IsNullOrWhiteSpace(errors) ? string.Empty : $" {errors}");
                Complete(viewModel, actions, message);
            }
            catch (Exception exception)
            {
                actions.Add(new ZetaWriteActionInfo { Timestamp = DateTime.Now, ErrorMessage = exception.Message });
                ErrorReporter.Report(app, "Ошибка записи ζ в комментарии", exception, launchLogPath);
                Complete(viewModel, actions, $"Запись ζ: ошибка Transaction/ExternalEvent. {exception.Message}");
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

        private static ZetaWriteActionInfo CreateBaseAction(LocalResistanceCalculationInfo row)
        {
            return new ZetaWriteActionInfo
            {
                Timestamp = DateTime.Now,
                ElementId = row.ElementId,
                PathIndex = row.PathIndex,
                RequestedZeta = 0,
                OverrideKey = row.OverrideKey,
                PathDependent = row.PathDependent,
                OverrideStorageType = row.PathDependent ? "DataStorage" : "Comment",
                VerificationMode = row.PathDependent ? ZetaVerificationMode.RemoveDataStorageOverride : ZetaVerificationMode.WriteExpectedZeta
            };
        }

        private static bool TryResolveRequestedZeta(LocalResistanceCalculationInfo row, out double zeta, out string errorMessage)
        {
            if (row.ManualZeta.HasValue)
            {
                zeta = row.ManualZeta.GetValueOrDefault();
            }
            else if (row.EffectiveZeta > 0)
            {
                zeta = row.EffectiveZeta;
            }
            else if (row.AutoZeta > 0)
            {
                zeta = row.AutoZeta;
            }
            else
            {
                zeta = 0;
                errorMessage = "Нет значения ζ для записи.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static Parameter? FindCommentsParameter(Element element, out string parameterName)
        {
            Parameter? parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (parameter != null)
            {
                parameterName = BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS.ToString();
                return parameter;
            }

            parameter = element.LookupParameter("Комментарии");
            if (parameter != null)
            {
                parameterName = "Комментарии";
                return parameter;
            }

            parameter = element.LookupParameter("Comments");
            parameterName = parameter == null ? string.Empty : "Comments";
            return parameter;
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

        private void ReloadIfPossible(VentCalcCenterViewModel viewModel, UIDocument uiDocument, IReadOnlyCollection<ZetaWriteActionInfo> actions)
        {
            long? lastLoadedElementId = viewModel.LastLoadedElementId;
            if (actions.Any(action => action.WriteSucceeded) && lastLoadedElementId.HasValue)
            {
                VentCalcCenterData data = dataLoader.Load(uiDocument, viewModel.Settings.ToAerodynamicSettings(), new ElementId(lastLoadedElementId.GetValueOrDefault()));
                InvokeOnUiThread(viewModel, () => viewModel.CompleteLoad(data));
            }
        }

        private static int SaveProjectCatalog(Document document, VentCalcCenterViewModel viewModel)
        {
            var items = viewModel.ProjectZetaCatalogRows
                .Where(row => row.ProjectZeta.HasValue)
                .Select(row => new ProjectZetaCatalogItem
                {
                    PathRole = row.PathRole,
                    AutoZeta = row.AutoZeta,
                    ProjectZeta = row.ProjectZeta
                })
                .ToList();
            using var transaction = new Transaction(document, "VentCalc: сохранить каталог ζ проекта");
            transaction.Start();
            RevitZetaOverrideStorage.SaveProjectCatalog(document, items);
            transaction.Commit();
            return items.Count;
        }

        private static void ResetRowToAuto(Document document, LocalResistanceCalculationInfo row, ZetaWriteActionInfo action)
        {
            action.RequestedZeta = row.AutoZeta;
            if (row.PathDependent)
            {
                RevitZetaOverrideStorage.DeleteOverrides(document, new[] { row.OverrideKey });
                action.OverrideStorageType = "DataStorageReset";
                action.VerificationMode = ZetaVerificationMode.RemoveDataStorageOverride;
                action.WriteSucceeded = true;
                action.VerifiedAfterCommit = !RevitZetaOverrideStorage.ReadOverrides(document).Any(item => string.Equals(item.OverrideKey, row.OverrideKey, StringComparison.Ordinal));
                action.ActualCommentAfterCommit = action.VerifiedAfterCommit ? "DataStorage override removed" : "DataStorage override still exists";
                action.ErrorMessage = action.VerifiedAfterCommit ? string.Empty : "DataStorage override не удалён.";
                action.WriteSucceeded = action.VerifiedAfterCommit;
                return;
            }

            Element? element = document.GetElement(new ElementId(row.ElementId));
            if (element == null)
            {
                action.ErrorMessage = "Элемент не найден.";
                return;
            }

            Parameter? comments = FindCommentsParameter(element, out string parameterName);
            action.ParameterFound = comments != null;
            action.ParameterName = parameterName;
            action.ParameterIsReadOnly = comments?.IsReadOnly ?? false;
            action.StorageType = comments?.StorageType.ToString() ?? string.Empty;
            action.OverrideStorageType = "CommentReset";
            action.VerificationMode = ZetaVerificationMode.RemoveZetaToken;
            if (comments == null || comments.IsReadOnly || comments.StorageType != StorageType.String)
            {
                action.ErrorMessage = comments == null ? "Параметр Комментарии не найден." : comments.IsReadOnly ? "Параметр Комментарии недоступен для записи." : $"Параметр Комментарии имеет тип {comments.StorageType}, ожидался String.";
                return;
            }

            action.OldComment = comments.AsString() ?? string.Empty;
            action.NewComment = RemoveZetaComment(action.OldComment);
            comments.Set(action.NewComment);
            action.ActualCommentAfterCommit = comments.AsString() ?? string.Empty;
            action.ZetaTokenExistsAfterCommit = ContainsAnyZeta(action.ActualCommentAfterCommit);
            action.VerifiedAfterCommit = !action.ZetaTokenExistsAfterCommit;
            action.WriteSucceeded = action.VerifiedAfterCommit;
            action.ErrorMessage = action.WriteSucceeded ? string.Empty : "После удаления комментарий всё ещё содержит z/ζ/zeta.";
        }

        private static void ResetWholeProjectToAuto(Document document, List<ZetaWriteActionInfo> actions)
        {
            using var transaction = new Transaction(document, "VentCalc: вернуть весь проект к Auto");
            transaction.Start();
            RevitZetaOverrideStorage.ClearOverrides(document);
            RevitZetaOverrideStorage.ClearProjectCatalog(document);
            foreach (Element element in new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .Where(element => element.Category != null && IsSupportedLocalResistanceCategory((BuiltInCategory)element.Category.Id.Value)))
            {
                var action = new ZetaWriteActionInfo { Timestamp = DateTime.Now, ElementId = element.Id.Value, OverrideStorageType = "ProjectReset", VerificationMode = ZetaVerificationMode.RemoveZetaToken };
                actions.Add(action);
                Parameter? comments = FindCommentsParameter(element, out string parameterName);
                action.ParameterFound = comments != null;
                action.ParameterName = parameterName;
                if (comments == null || comments.IsReadOnly || comments.StorageType != StorageType.String)
                {
                    action.WriteSucceeded = comments == null;
                    action.VerifiedAfterCommit = comments == null;
                    action.VerificationMode = comments == null ? ZetaVerificationMode.ClearProjectCatalogValue : action.VerificationMode;
                    action.ErrorMessage = comments == null ? string.Empty : "Комментарии недоступны для очистки.";
                    continue;
                }

                action.OldComment = comments.AsString() ?? string.Empty;
                action.NewComment = RemoveZetaComment(action.OldComment);
                if (!string.Equals(action.OldComment, action.NewComment, StringComparison.Ordinal))
                {
                    comments.Set(action.NewComment);
                }
                action.ActualCommentAfterCommit = comments.AsString() ?? string.Empty;
                action.ZetaTokenExistsAfterCommit = ContainsAnyZeta(action.ActualCommentAfterCommit);
                action.WriteSucceeded = !action.ZetaTokenExistsAfterCommit;
                action.VerifiedAfterCommit = action.WriteSucceeded;
                action.ErrorMessage = action.WriteSucceeded ? string.Empty : "После очистки комментарий всё ещё содержит z/ζ/zeta.";
            }
            transaction.Commit();
        }

        private static bool IsSupportedLocalResistanceCategory(BuiltInCategory category)
        {
            return category == BuiltInCategory.OST_DuctFitting
                || category == BuiltInCategory.OST_DuctAccessory
                || category == BuiltInCategory.OST_DuctTerminal
                || category == BuiltInCategory.OST_MechanicalEquipment;
        }

        private static string RemoveZetaComment(string existingComment)
        {
            string withoutZeta = Regex.Replace(existingComment ?? string.Empty, @"(?:ζ|zeta|z)\s*=\s*[-+]?\d+(?:[\.,]\d+)?", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return Regex.Replace(withoutZeta, @"\s{2,}", " ").Trim();
        }

        private static bool ContainsAnyZeta(string comment)
        {
            return Regex.IsMatch(comment ?? string.Empty, @"(?:ζ|zeta|z)\s*=\s*[-+]?\d+(?:[\.,]\d+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void SavePathDependentOverride(Document document, LocalResistanceCalculationInfo row, double zeta)
        {
            var zetaOverride = new ZetaOverrideInfo
            {
                SystemName = ExtractSystemName(row.OverrideKey),
                ElementId = row.ElementId,
                PathRole = row.PathRole,
                PreviousDuctElementId = row.PreviousDuctElementId,
                NextDuctElementId = row.NextDuctElementId,
                Zeta = zeta,
                OverrideKey = row.OverrideKey
            };
            RevitZetaOverrideStorage.SaveOverrides(document, new[] { zetaOverride });
        }

        private static void VerifyStoredOverrides(Document document, IEnumerable<ZetaWriteActionInfo> actions)
        {
            IReadOnlyList<ZetaOverrideInfo> overrides = RevitZetaOverrideStorage.ReadOverrides(document);
            foreach (ZetaWriteActionInfo action in actions.Where(action => action.PathDependent && action.WriteSucceeded))
            {
                ZetaOverrideInfo? found = overrides.FirstOrDefault(item => string.Equals(item.OverrideKey, action.OverrideKey, StringComparison.Ordinal));
                if (action.VerificationMode == ZetaVerificationMode.RemoveDataStorageOverride)
                {
                    action.VerifiedAfterCommit = found == null;
                    action.WriteSucceeded = action.VerifiedAfterCommit;
                    action.ActualCommentAfterCommit = found == null ? "DataStorage override removed" : "DataStorage override still exists";
                    action.ErrorMessage = action.VerifiedAfterCommit ? string.Empty : "После commit переопределение VentCalc DataStorage не удалено.";
                    continue;
                }

                action.VerifiedAfterCommit = found != null && Math.Abs(found.Zeta - action.RequestedZeta) <= 0.0001;
                action.WriteSucceeded = action.VerifiedAfterCommit;
                action.ActualCommentAfterCommit = found == null ? string.Empty : $"DataStorage ζ={found.Zeta.ToString("0.###", CultureInfo.InvariantCulture)}";
                if (!action.VerifiedAfterCommit)
                {
                    action.ErrorMessage = "После commit переопределение VentCalc DataStorage не найдено.";
                }
            }
        }

        private static string ExtractSystemName(string overrideKey)
        {
            int separator = (overrideKey ?? string.Empty).IndexOf('|');
            return separator < 0 ? string.Empty : overrideKey.Substring(0, separator);
        }

        private static void VerifyCommittedComments(Document document, IEnumerable<ZetaWriteActionInfo> actions)
        {
            foreach (ZetaWriteActionInfo action in actions.Where(action => action.WriteSucceeded
                && (action.VerificationMode == ZetaVerificationMode.WriteExpectedZeta || action.VerificationMode == ZetaVerificationMode.RemoveZetaToken)
                && (action.OverrideStorageType == "Comment" || action.OverrideStorageType == "CommentReset" || action.OverrideStorageType == "ProjectReset")))
            {
                try
                {
                    Element? element = document.GetElement(new ElementId(action.ElementId));
                    if (element == null)
                    {
                        action.WriteSucceeded = false;
                        action.VerifiedAfterCommit = false;
                        action.ErrorMessage = "Элемент не найден после Transaction.Commit().";
                        continue;
                    }

                    Parameter? comments = FindCommentsParameter(element, out string parameterName);
                    action.ParameterFound = comments != null;
                    action.ParameterName = string.IsNullOrWhiteSpace(action.ParameterName) ? parameterName : action.ParameterName;
                    action.ParameterIsReadOnly = comments?.IsReadOnly ?? action.ParameterIsReadOnly;
                    action.StorageType = comments?.StorageType.ToString() ?? action.StorageType;
                    if (comments == null)
                    {
                        action.WriteSucceeded = false;
                        action.VerifiedAfterCommit = false;
                        action.ErrorMessage = "Параметр комментариев не найден после записи.";
                        continue;
                    }

                    string actualComment = comments.AsString() ?? string.Empty;
                    action.ActualCommentAfterCommit = actualComment;
                    action.ZetaTokenExistsAfterCommit = ContainsAnyZeta(actualComment);
                    action.VerifiedAfterCommit = action.VerificationMode == ZetaVerificationMode.RemoveZetaToken
                        ? !action.ZetaTokenExistsAfterCommit
                        : ContainsExpectedZeta(actualComment, action.RequestedZeta);
                    action.WriteSucceeded = action.VerifiedAfterCommit;
                    if (!action.VerifiedAfterCommit)
                    {
                        action.ErrorMessage = action.VerificationMode == ZetaVerificationMode.RemoveZetaToken
                            ? $"После commit комментарий всё ещё содержит z/ζ/zeta. Фактический комментарий: '{actualComment}'."
                            : $"После commit ожидаемое ζ={action.RequestedZeta.ToString("0.###", CultureInfo.InvariantCulture)} не найдено. Фактический комментарий: '{actualComment}'.";
                    }
                }
                catch (Exception exception)
                {
                    action.WriteSucceeded = false;
                    action.VerifiedAfterCommit = false;
                    action.ErrorMessage = exception.Message;
                }
            }
        }

        private static bool ContainsExpectedZeta(string comment, double expectedZeta)
        {
            const string pattern = @"(?:ζ|zeta|z)\s*=\s*(?<value>[-+]?\d+(?:[\.,]\d+)?)";
            foreach (Match match in Regex.Matches(comment ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                string value = match.Groups["value"].Value;
                if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double actualZeta)
                    && Math.Abs(actualZeta - expectedZeta) <= 0.0001)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Complete(VentCalcCenterViewModel viewModel, IReadOnlyCollection<ZetaWriteActionInfo> actions, string message)
        {
            InvokeOnUiThread(viewModel, () => viewModel.CompleteZetaCommentWrite(actions, message));
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
