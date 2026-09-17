using System;
using System.IO;
using System.Linq;
using System.Text;
using AccC3DMetadata.Config;
using AccC3DMetadata.Models;
using AccC3DMetadata.Services;
using AccC3DMetadata.UI;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

namespace AccC3DMetadata
{
    /// <summary>
    /// Registers the ACC Sync typed commands with AutoCAD.
    /// Each sync command is <see cref="CommandFlags.Modal"/> so AutoCAD blocks other input
    /// while the sync operation is in progress.
    /// </summary>
    /// <remarks>
    /// Commands are <c>async void</c> because AutoCAD's command infrastructure does not
    /// support <c>Task</c>-returning methods. Exceptions are caught and reported via the
    /// command line — they must not propagate to the AutoCAD runtime.
    /// </remarks>
    public class Commands : Functions
    {
        /// <summary>
        /// Pulls attribute values from ACC into the open DWG (ACC → DWG, read-only direction).
        /// Invoked via ribbon button or by typing <c>AccSyncPull</c> at the command line.
        /// </summary>
        [CommandMethod("ACCSYNC", "AccSyncPull", CommandFlags.Modal)]
        public static async void AccSyncPull()
        {
            SyncProgressDialog progressDlg = null;
            try
            {
                progressDlg = new SyncProgressDialog("Pull from Autodesk Forma");
                progressDlg.Show();

                var orchestrator = new SyncOrchestrator(AcadDoc);
                progressDlg.UpdateStatus("Loading configuration…");
                var config = await orchestrator.LoadConfigAsync();

                var result = await orchestrator.RunAsync(
                    config,
                    SyncDirection.Read,
                    new Progress<string>(msg => progressDlg.UpdateStatus(msg))
                );

                ed.WriteMessage(
                    $"\nPull complete — {result.MappingsApplied} mapping(s) applied, {result.Errors} error(s).\n"
                );
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nAccSyncPull failed: {ex.Message}\n");
            }
            finally
            {
                progressDlg?.Close();
            }
        }

        /// <summary>
        /// Pushes attribute values from the open DWG to ACC (DWG → ACC, write-only direction).
        /// Invoked via ribbon button or by typing <c>AccSyncPush</c> at the command line.
        /// </summary>
        [CommandMethod("ACCSYNC", "AccSyncPush", CommandFlags.Modal)]
        public static async void AccSyncPush()
        {
            SyncProgressDialog progressDlg = null;
            try
            {
                progressDlg = new SyncProgressDialog("Push to Autodesk Forma");
                progressDlg.Show();

                var orchestrator = new SyncOrchestrator(AcadDoc);
                progressDlg.UpdateStatus("Loading configuration…");
                var config = await orchestrator.LoadConfigAsync();

                var result = await orchestrator.RunAsync(
                    config,
                    SyncDirection.Write,
                    new Progress<string>(msg => progressDlg.UpdateStatus(msg))
                );

                ed.WriteMessage(
                    $"\nPush complete — {result.MappingsApplied} mapping(s) applied, {result.Errors} error(s).\n"
                );
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nAccSyncPush failed: {ex.Message}\n");
            }
            finally
            {
                progressDlg?.Close();
            }
        }

        /// <summary>
        /// Performs a bidirectional sync between the open DWG and ACC, showing the
        /// <see cref="ConflictResolutionDialog"/> for any mappings with conflicting values
        /// and a <see cref="ConflictStrategy.Prompt"/> strategy.
        /// Invoked via ribbon button or by typing <c>AccSyncBoth</c> at the command line.
        /// </summary>
        [CommandMethod("ACCSYNC", "AccSyncBoth", CommandFlags.Modal)]
        public static async void AccSyncBoth()
        {
            SyncProgressDialog progressDlg = null;
            try
            {
                progressDlg = new SyncProgressDialog("Sync with Autodesk Forma");
                progressDlg.Show();

                var orchestrator = new SyncOrchestrator(AcadDoc);
                progressDlg.UpdateStatus("Loading configuration…");
                var config = await orchestrator.LoadConfigAsync();

                var result = await orchestrator.RunAsync(
                    config,
                    SyncDirection.ReadWrite,
                    new Progress<string>(msg => progressDlg.UpdateStatus(msg))
                );

                ed.WriteMessage(
                    $"\nSync complete — {result.MappingsApplied} mapping(s), "
                        + $"{result.ConflictsResolved} conflict(s) resolved, "
                        + $"{result.ConflictsCancelled} conflict(s) cancelled, "
                        + $"{result.Errors} error(s).\n"
                );
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nAccSyncBoth failed: {ex.Message}\n");
            }
            finally
            {
                progressDlg?.Close();
            }
        }

        /// <summary>
        /// Loads and displays the mapping configuration for the open drawing without performing
        /// any sync. Useful for verifying that the config file is correctly placed and parsed.
        /// Invoked via ribbon button or by typing <c>AccSyncLoadConfig</c> at the command line.
        /// </summary>
        [CommandMethod("ACCSYNC", "AccSyncLoadConfig", CommandFlags.Modal)]
        public static async void AccSyncLoadConfig()
        {
            try
            {
                var orchestrator = new SyncOrchestrator(AcadDoc);
                var config = await orchestrator.LoadConfigAsync();

                ed.WriteMessage($"\nConfig loaded successfully.");
                ed.WriteMessage(
                    $"\n  Hub:      {config.HubId ?? "(resolved from Desktop Connector path)"}"
                );
                ed.WriteMessage(
                    $"\n  Project:  {config.ProjectId ?? "(resolved from Desktop Connector path)"}"
                );
                ed.WriteMessage(
                    $"\n  Item:     {config.DrawingItemId ?? "(resolved from Desktop Connector path)"}"
                );
                ed.WriteMessage($"\n  Mappings: {config.Mappings.Count}");

                foreach (var m in config.Mappings)
                {
                    string target =
                        m.Target == MappingTarget.BlockAttribute
                            ? $"{m.BlockName}.{m.BlockAttributeTag}"
                            : $"{m.PropertySetName}.{m.PropertyName}";
                    ed.WriteMessage(
                        $"\n    [{m.Direction}] {m.AccAttributeName} → {m.Target}:{target}"
                    );
                }
                ed.WriteMessage("\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nAccSyncLoadConfig failed: {ex.Message}\n");
            }
        }

        /// <summary>
        /// Opens the Settings dialog where the user can enter and save their APS Client ID.
        /// The ID is stored in the user profile and persists across plugin updates.
        /// Invoked via ribbon button or by typing <c>AccSyncSettings</c> at the command line.
        /// </summary>
        [CommandMethod("ACCSYNC", "AccSyncSettings", CommandFlags.Modal)]
        public static void AccSyncSettings()
        {
            try
            {
                var dlg = new ClientIdSettingsDialog();
                bool saved = Application.ShowModalWindow(dlg) == true;
                if (saved)
                    ed.WriteMessage(
                        "\nAPS Client ID saved. It will be used for the next authentication.\n"
                    );
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nAccSyncSettings failed: {ex.Message}\n");
            }
        }

        /// <summary>
        /// Creates or edits the <c>accsync.xml</c> config for the open drawing's folder: pick a
        /// block, choose which of its attributes to sync, then save locally and push straight to ACC.
        /// Invoked via ribbon button or by typing <c>AccSyncConfigEditor</c> at the command line.
        /// </summary>
        [CommandMethod("ACCSYNC", "AccSyncConfigEditor", CommandFlags.Modal)]
        public static async void AccSyncConfigEditor()
        {
            try
            {
                ed.WriteMessage("\nAuthenticating with Autodesk Platform Services…");
                string token = await TokenCache.GetAccessTokenAsync();
                if (token == null)
                {
                    ed.WriteMessage("\nAccSyncConfigEditor cancelled — no access token.\n");
                    return;
                }

                string dwgPath = AcadDoc.Database.Filename;
                string dwgDir = Path.GetDirectoryName(dwgPath) ?? string.Empty;
                string configPath = Path.Combine(dwgDir, "accsync.xml");

                SyncConfig existingConfig = File.Exists(configPath)
                    ? SyncConfigParser.Parse(await File.ReadAllTextAsync(configPath))
                    : null;

                System.Collections.Generic.List<(
                    string BlockName,
                    System.Collections.Generic.List<string> AttributeTags
                )> blocks;
                AcadDoc.LockDocument();
                using (var tr = AcadDoc.Database.TransactionManager.StartTransaction())
                {
                    blocks = DwgBlockService
                        .GetAllBlocksWithAttributes(tr, AcadDoc.Database)
                        .ToList();
                    tr.Commit();
                }

                if (blocks.Count == 0)
                {
                    Application.ShowAlertDialog(
                        "No blocks with attributes were found in the current drawing."
                    );
                    return;
                }

                var dlg = new ConfigEditorDialog(blocks, existingConfig);
                bool accepted = Application.ShowModalWindow(dlg) == true;
                if (!accepted)
                {
                    ed.WriteMessage("\nConfig editor cancelled.\n");
                    return;
                }

                string xml = SyncConfigParser.Serialize(dlg.Result);

                // Mirror the file locally so the sync commands (which only ever read the local
                // file system) see it immediately, in addition to pushing it straight to ACC.
                await File.WriteAllTextAsync(configPath, xml);

                ed.WriteMessage("\nResolving ACC hub, project and folder for this drawing…");
                var acc = new AccFileService();
                string projectId,
                    folderId;

                var dcInfo = DesktopConnectorService.TryResolveDrawingLocation(dwgPath);
                if (dcInfo?.ProjectId != null && dcInfo.FolderUrn != null)
                {
                    projectId = dcInfo.ProjectId;
                    folderId = dcInfo.FolderUrn;
                }
                else
                {
                    var resolved = await acc.ResolveItemFromDrawingPathAsync(dwgPath, token);
                    projectId = resolved.projectId;
                    folderId =
                        await acc.GetItemParentFolderIdAsync(
                            resolved.projectId,
                            resolved.itemId,
                            token
                        )
                        ?? throw new InvalidOperationException(
                            "Could not resolve the ACC folder for this drawing."
                        );
                }

                ed.WriteMessage("\nUploading accsync.xml to Autodesk Forma…");
                await acc.UploadFileToFolderAsync(
                    projectId,
                    folderId,
                    "accsync.xml",
                    Encoding.UTF8.GetBytes(xml),
                    token
                );

                ed.WriteMessage(
                    $"\nConfig saved — {dlg.Result.Mappings.Count} mapping(s) written to {configPath}.\n"
                );
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nAccSyncConfigEditor failed: {ex.Message}\n");
            }
        }
    }
}
