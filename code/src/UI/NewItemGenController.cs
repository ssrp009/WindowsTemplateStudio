﻿// ******************************************************************
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THE CODE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
// DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH
// THE CODE OR THE USE OR OTHER DEALINGS IN THE CODE.
// ******************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.Templates.Core;
using Microsoft.Templates.Core.Diagnostics;
using Microsoft.Templates.Core.Gen;
using Microsoft.Templates.Core.PostActions;
using Microsoft.Templates.Core.PostActions.Catalog.Merge;
using Microsoft.VisualStudio.TemplateWizard;
using Newtonsoft.Json;

namespace Microsoft.Templates.UI
{
    public class NewItemGenController : GenController
    {
        private static Lazy<NewItemGenController> _instance = new Lazy<NewItemGenController>(Initialize);
        public static NewItemGenController Instance => _instance.Value;


        private static NewItemGenController Initialize()
        {
            return new NewItemGenController(new NewItemPostActionFactory());
        }

        private NewItemGenController(PostActionFactory postactionFactory)
        {
            _postactionFactory = postactionFactory;
        }

        public (string ProjectType, string Framework) ReadProjectConfiguration()
        {
            //TODO: Review this
            var path = Path.Combine(GenContext.Current.ProjectPath, "Package.appxmanifest");
            if (File.Exists(path))
            {
                var manifest = XElement.Load(path);

                var metadata = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "Metadata");
                var projectType = metadata?.Descendants().FirstOrDefault(m => m.Attribute("Name").Value == "projectType")?.Attribute("Value")?.Value;
                var framework = metadata?.Descendants().FirstOrDefault(m => m.Attribute("Name").Value == "framework")?.Attribute("Value")?.Value;

                return (projectType, framework);
            }
            
            return (string.Empty, string.Empty);
        }


        public UserSelection GetUserSelectionNewItem(TemplateType templateType)
        {
            var newItem = new Views.NewItem.MainView(templateType);

            try
            {
                CleanStatusBar();

                GenContext.ToolBox.Shell.ShowModal(newItem);
                if (newItem.Result != null)
                {
                    //TODO: Review when right-click-actions available to track Project or Page completed.
                    //AppHealth.Current.Telemetry.TrackWizardCompletedAsync(WizardTypeEnum.NewItem).FireAndForget();

                    return newItem.Result;
                }
                else
                {
                    //TODO: Review when right-click-actions available to track Project or Page cancelled.
                    //AppHealth.Current.Telemetry.TrackWizardCancelledAsync(WizardTypeEnum.NewItem).FireAndForget();
                }

            }
            catch (Exception ex) when (!(ex is WizardBackoutException))
            {
                newItem.SafeClose();
                ShowError(ex);
            }

            GenContext.ToolBox.Shell.CancelWizard();

            return null;
        }


        public async Task GenerateNewItemAsync(UserSelection userSelection)
        {
            try
            {
               await UnsafeGenerateNewItemAsync(userSelection);
            }
            catch (Exception ex)
            {
                ShowError(ex, userSelection);

                GenContext.ToolBox.Shell.CancelWizard(false);
            }
        }

        public async Task UnsafeGenerateNewItemAsync(UserSelection userSelection)
        {
            var genItems = GenComposer.ComposeNewItem(userSelection).ToList();
            var chrono = Stopwatch.StartNew();

            var genResults = await GenerateItemsAsync(genItems);

            chrono.Stop();

            // TODO: Review New Item telemetry
            TrackTelemery(genItems, genResults, chrono.Elapsed.TotalSeconds, userSelection.ProjectType, userSelection.Framework);
        }

        //public CompareResult ShowLastActionResult()
        //{
        //    //var newItem = new Views.NewItem.NewItemView();
        //    var undoLastAction = new Views.NewItem.UndoLastActionView();

        //    try
        //    {
        //        CleanStatusBar();

        //        GenContext.ToolBox.Shell.ShowModal(undoLastAction);
        //        if (undoLastAction.Result != null)
        //        {
        //            //TODO: Review when right-click-actions available to track Project or Page completed.
        //            //AppHealth.Current.Telemetry.TrackWizardCompletedAsync(WizardTypeEnum.NewItem).FireAndForget();

        //            return undoLastAction.Result;
        //        }
        //        else
        //        {
        //            //TODO: Review when right-click-actions available to track Project or Page cancelled.
        //            //AppHealth.Current.Telemetry.TrackWizardCancelledAsync(WizardTypeEnum.NewItem).FireAndForget();
        //        }

        //    }
        //    catch (Exception ex) when (!(ex is WizardBackoutException))
        //    {
        //        undoLastAction.SafeClose();
        //        ShowError(ex);
        //    }

        //    GenContext.ToolBox.Shell.CancelWizard();

        //    return null;
        //}

        public CompareResult CompareOutputAndProject()
        {
            var result = new CompareResult();
            var files = Directory
                .EnumerateFiles(GenContext.Current.OutputPath, "*", SearchOption.AllDirectories)
                .Where(f => !Regex.IsMatch(f, MergePostAction.PostactionRegex) && !Regex.IsMatch(f, MergePostAction.FailedPostactionRegex))
                .ToList();

            foreach (var file in files)
            {
                var destFilePath = file.Replace(GenContext.Current.OutputPath, GenContext.Current.ProjectPath);
                if (!File.Exists(destFilePath))
                {
                    result.NewFiles.Add(file.Replace(GenContext.Current.OutputPath + Path.DirectorySeparatorChar, String.Empty));
                }
                else
                {
                    if (GenContext.Current.MergeFilesFromProject.Contains(destFilePath))
                    {
                        if (!FilesAreEqual(file, destFilePath))
                        {
                            result.ModifiedFiles.Add(destFilePath.Replace(GenContext.Current.ProjectPath + Path.DirectorySeparatorChar, String.Empty));
                        }
                    }
                    else
                    {
                        if (!FilesAreEqual(file, destFilePath))
                        {
                            result.ConflictingFiles.Add(destFilePath.Replace(GenContext.Current.ProjectPath + Path.DirectorySeparatorChar, String.Empty));
                        }
                    }
                }
            }

            return result;
        }

        public void SyncNewItem(UserSelection userSelection)
        {
            try
            {
                UnsafeSyncNewItem();
            }
            catch (Exception ex)
            {
                ShowError(ex, userSelection);
                GenContext.ToolBox.Shell.CancelWizard(false);
            }
        }

        public void UnsafeSyncNewItem()
        {
            var result = CompareOutputAndProject();

            //BackupProjectFiles(result);
            CopyFilesToProject(result);

            ExecuteFinishGenerationPostActions();
            CleanupTempGeneration();

        }

        private void BackupProjectFiles(CompareResult result)
        {
            var projectGuid = GenContext.ToolBox.Shell.GetActiveProjectGuid();

            if (string.IsNullOrEmpty(projectGuid))
            {
                //TODO: Handle this 
                return;
            }

            var backupFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Configuration.Current.BackupFolderName,
                projectGuid);

            var fileName = Path.Combine(backupFolder, "backup.json");

            if (!Directory.Exists(backupFolder))
            {
                Directory.CreateDirectory(backupFolder);
            }
            else
            {
                Directory.Delete(backupFolder, true);
                Directory.CreateDirectory(backupFolder);
            }

            File.WriteAllText(fileName, JsonConvert.SerializeObject(result));

            var modifiedFiles = result.ConflictingFiles.Concat(result.ModifiedFiles);

            foreach (var file in modifiedFiles)
            {
                var originalFile = Path.Combine(GenContext.Current.ProjectPath, file);
                var backupFile = Path.Combine(backupFolder, file);
                var destDirectory = Path.GetDirectoryName(backupFile);
                if (!Directory.Exists(destDirectory))
                {
                    Directory.CreateDirectory(destDirectory);
                }
                File.Copy(originalFile, backupFile, true);
            }
        }

        public void CleanupTempGeneration()
        {
            GenContext.Current.GenerationWarnings.Clear();
            GenContext.Current.MergeFilesFromProject.Clear();
            GenContext.Current.ProjectItems.Clear();
            var directory = GenContext.Current.OutputPath;
            try
            {
                if (directory.Contains(Path.GetTempPath()))
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }
                }
            }
            catch (Exception ex)
            {
                var msg = $"The folder {directory} can't be delete. Error: {ex.Message}";
                AppHealth.Current.Warning.TrackAsync(msg, ex).FireAndForget();
            }
        }

        //public CompareResult GetLastActionInfo()
        //{
        //    var projectGuid = GenContext.ToolBox.Shell.GetActiveProjectGuid();

        //    if (string.IsNullOrEmpty(projectGuid))
        //    {
        //        //TODO: Handle this 
        //        return null;
        //    }

        //    var backupFolder = Path.Combine(
        //       Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        //       Configuration.Current.BackupFolderName,
        //       projectGuid);

        //    var fileName = Path.Combine(backupFolder, "backup.json");

        //    if (!Directory.Exists(backupFolder))
        //    {
        //        //TODO: Handle this
        //    }

        //    return JsonConvert.DeserializeObject<CompareResult>(File.ReadAllText(fileName));

        //}

        //public void UndoLastAction(CompareResult result)
        //{
        //    var projectGuid = GenContext.ToolBox.Shell.GetActiveProjectGuid();

        //    var backupFolder = Path.Combine(
        //       Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        //       Configuration.Current.BackupFolderName,
        //       projectGuid);

        //    var modifiedFiles = result.ConflictingFiles.Concat(result.ModifiedFiles);

        //    foreach (var file in modifiedFiles)
        //    {
        //        var originalFile = Path.Combine(GenContext.Current.ProjectPath, file);
        //        var backupFile = Path.Combine(backupFolder, file);

        //        File.Copy(backupFile, originalFile, true);
        //    }

        //    foreach (var file in result.NewFiles)
        //    {
        //        var projectFile = Path.Combine(GenContext.Current.ProjectPath, file);
        //        File.Delete(projectFile);
        //        //TODO:Remove file from project
        //    }
        //}

        private void CopyFilesToProject(CompareResult result)
        {
            var modifiedFiles = result.ConflictingFiles.Concat(result.ModifiedFiles);

            foreach (var file in modifiedFiles)
            {
                var sourceFile = Path.Combine(GenContext.Current.OutputPath, file);
                var destFileName = Path.Combine(GenContext.Current.ProjectPath, file);
                File.Copy(sourceFile, destFileName, true);
            }

            foreach (var file in result.NewFiles)
            {
                var sourceFile = Path.Combine(GenContext.Current.OutputPath, file);
                var destFileName = Path.Combine(GenContext.Current.ProjectPath, file);
                var destDirectory = Path.GetDirectoryName(destFileName);
                if (!Directory.Exists(destDirectory))
                {
                    Directory.CreateDirectory(destDirectory);
                }
                File.Copy(sourceFile, destFileName, true);
            }
        }

        private static bool FilesAreEqual(string file, string destFilePath)
        {
            return File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(destFilePath));
        }


        private static void TrackTelemery(IEnumerable<GenInfo> genItems, Dictionary<string, TemplateCreationResult> genResults, double timeSpent, string appProjectType, string appFx)
        {
            try
            {
                int pagesAdded = genItems.Where(t => t.Template.GetTemplateType() == TemplateType.Page).Count();
                int featuresAdded = genItems.Where(t => t.Template.GetTemplateType() == TemplateType.Feature).Count();

                foreach (var genInfo in genItems)
                {
                    if (genInfo.Template == null)
                    {
                        continue;
                    }

                    string resultsKey = $"{genInfo.Template.Identity}_{genInfo.Name}";

                    if (genInfo.Template.GetTemplateType() == TemplateType.Project)
                    {
                        AppHealth.Current.Telemetry.TrackProjectGenAsync(genInfo.Template, 
                            appProjectType, appFx, genResults[resultsKey], pagesAdded, featuresAdded, timeSpent).FireAndForget();
                    }
                    else
                    {
                        AppHealth.Current.Telemetry.TrackItemGenAsync(genInfo.Template, appProjectType, appFx, genResults[resultsKey]).FireAndForget();
                    }
                }
            }
            catch (Exception ex)
            {
                AppHealth.Current.Exception.TrackAsync(ex, "Exception tracking telemetry for Template Generation.").FireAndForget();
            }
        }
    }
}