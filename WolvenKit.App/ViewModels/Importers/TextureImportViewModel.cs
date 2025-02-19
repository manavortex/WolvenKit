﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using ReactiveUI.Fody.Helpers;
using WolvenKit.App.Models;
using WolvenKit.App.ViewModels.Tools;
using WolvenKit.Common;
using WolvenKit.Common.Extensions;
using WolvenKit.Common.Interfaces;
using WolvenKit.Common.Model;
using WolvenKit.Common.Model.Arguments;
using WolvenKit.Common.Services;
using WolvenKit.Core.Interfaces;
using WolvenKit.Core.Services;
using WolvenKit.Functionality.Controllers;
using WolvenKit.Functionality.Converters;
using WolvenKit.Functionality.Services;
using WolvenKit.Modkit.RED4.Opus;
using WolvenKit.RED4.Archive;
using WolvenKit.RED4.CR2W;
using WolvenKit.ViewModels.Tools;

namespace WolvenKit.App.ViewModels.Importers;

public abstract partial class ImportViewModel : ImportExportViewModel
{

}

public partial class TextureImportViewModel : ImportViewModel
{
    public TextureImportViewModel(
        IGameControllerFactory gameController,
        ISettingsManager settingsManager,
        IWatcherService watcherService,
        ILoggerService loggerService,
        IProjectManager projectManager,
        INotificationService notificationService,
        IArchiveManager archiveManager,
        IPluginService pluginService,
        IModTools modTools,
        IProgressService<double> progressService)
    {
        _gameController = gameController;
        _settingsManager = settingsManager;
        _watcherService = watcherService;
        _loggerService = loggerService;
        _projectManager = projectManager;
        _notificationService = notificationService;
        _archiveManager = archiveManager;
        _pluginService = pluginService;
        _modTools = modTools;
        _progressService = progressService;

        LoadFiles();

        Header = Name;
    }

    public override string Name => "Import Tool";


    #region Commands

    [RelayCommand(CanExecute = nameof(IsAnyFileSelected))]
    private void ImportSettings()
    {
        foreach (var item in Items.Where(x => x.IsChecked))
        {
            if (item.Properties is not XbmImportArgs xbmImportArgs)
            {
                continue;
            }

            xbmImportArgs = (item as ImportableItemViewModel).LoadXbmDefaultSettings();
            _loggerService?.Info($"Load settings for \"{item.Name}\": Parsed filename");
        }
    }

    #endregion

    protected override async Task ExecuteProcessBulk(bool all = false)
    {
        IsProcessing = true;
        _watcherService.IsSuspended = true;
        var progress = 0;
        _progressService.Report(0);

        var total = 0;
        var sucessful = 0;

        //prepare a list of failed items
        var failedItems = new List<string>();

        var toBeImported = Items
            .Where(_ => all || _.IsChecked)
            .Where(x => !x.Extension.Equals(ERawFileFormat.wav.ToString()))
            .Cast<ImportableItemViewModel>()
            .ToList();
        total = toBeImported.Count;
        foreach (var item in toBeImported)
        {
            if (await Task.Run(() => ImportSingleTask(item)))
            {
                sucessful++;
            }
            else // not successful
            {
                failedItems.Add(item.FullName);
            }

            Interlocked.Increment(ref progress);
            _progressService.Report(progress / (float)total);
        }

        await ImportWavs(Items.Where(_ => all || _.IsChecked)
            .Where(x => x.Extension.Equals(ERawFileFormat.wav.ToString()))
            .Select(x => x.FullName)
            .ToList()
            );

        IsProcessing = false;

        _watcherService.IsSuspended = false;
        await _watcherService.RefreshAsync(_projectManager.ActiveProject);
        _progressService.IsIndeterminate = false;

        _notificationService.Success($"{sucessful}/{total} files have been processed and are available in the Project Explorer");
        _loggerService.Success($"{sucessful}/{total} files have been processed and are available in the Project Explorer");

        //We format the list of failed export/import items here
        if (failedItems.Count > 0)
        {
            var failedItemsErrorString = $"The following items failed:\n{string.Join("\n", failedItems)}";
            _notificationService.Error(failedItemsErrorString); //notify once only 
            _loggerService.Error(failedItemsErrorString);
        }

        _progressService.Completed();
    }

    private Task<bool> ImportWavs(List<string> wavs)
    {
        var proj = _projectManager.ActiveProject;
        if (_gameController.GetController() is RED4Controller cp77Controller)
        {
            OpusTools opusTools = new(
                proj.ModDirectory,
                proj.RawDirectory,
                true);

            return Task.Run(() => opusTools.ImportWavs(wavs.ToArray()));
        }

        return Task.FromResult(false);
    }

    private async Task<bool> ImportSingleTask(ImportableItemViewModel item)
    {
        if (_gameController.GetController() is not RED4Controller)
        {
            return false;
        }

        var proj = _projectManager.ActiveProject;
        var fi = new FileInfo(item.FullName);
        if (fi.Exists)
        {
            if (item.Properties is GltfImportArgs gltfImportArgs)
            {
                gltfImportArgs.Archives = _archiveManager.Archives.Items.Cast<ICyberGameArchive>().ToList();
                gltfImportArgs.Archives.Insert(0, new FileSystemArchive(_projectManager.ActiveProject.ModDirectory));
            }

            if (item.Properties is ReImportArgs reImportArgs)
            {
                if (!_pluginService.IsInstalled(EPlugin.redmod))
                {
                    _loggerService.Error("Redmod plugin needs to be installed to import animations");
                    return false;
                }

                reImportArgs.Depot = proj.ModDirectory;
                reImportArgs.RedMod = Path.Combine(_settingsManager.GetRED4GameRootDir(), "tools", "redmod", "bin", "redMod.exe");
            }

            var settings = new GlobalImportArgs().Register(item.Properties as ImportArgs);
            var rawDir = new DirectoryInfo(proj.RawDirectory);
            var redrelative = new RedRelativePath(rawDir, fi.GetRelativePath(rawDir));

            try
            {
                return await _modTools.Import(redrelative, settings, new DirectoryInfo(proj.ModDirectory));
            }
            catch (Exception e)
            {
                _loggerService.Error($"Could not import {item.Name}");
                _loggerService.Error(e);
            }

        }

        return false;
    }

    protected override void LoadFiles()
    {
        if (_projectManager.ActiveProject is null)
        {
            return;
        }

        var files = Directory.GetFiles(_projectManager.ActiveProject.RawDirectory, "*", SearchOption.AllDirectories)
            .Where(CanImport)
            .Select(x => new ImportableItemViewModel(x));

        Items.Clear();
        Items = new(files);
    }

    private static bool CanImport(string x) => Enum.TryParse<ERawFileFormat>(Path.GetExtension(x).TrimStart('.'), out var _);
}
