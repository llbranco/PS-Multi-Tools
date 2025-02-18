﻿Imports System.ComponentModel
Imports System.IO
Imports System.Security.Authentication
Imports System.Windows.Media.Animation
Imports FluentFTP
Imports PS_Multi_Tools.INI
Imports psmt_lib

Public Class PS3Library

    Dim WithEvents GameLoaderWorker As New BackgroundWorker() With {.WorkerReportsProgress = True}
    Dim WithEvents NewLoadingWindow As New SyncWindow() With {.Title = "Loading PS3 files", .ShowActivated = True}

    Dim GamesList As New List(Of PS3Game)()
    Dim ConsoleIP As String = ""
    Dim ConsolePort As String = ""
    Dim FoldersCount As Integer = 0
    Dim PKGCount As Integer = 0
    Dim ISOCount As Integer = 0
    Dim IsSoundPlaying As Boolean = False

    'Games context menu items
    Dim WithEvents NewContextMenu As New Controls.ContextMenu()
    Dim WithEvents CopyToMenuItem As New Controls.MenuItem() With {.Header = "Copy to", .Icon = New Controls.Image() With {.Source = New BitmapImage(New Uri("/Images/copy-icon.png", UriKind.Relative))}}
    Dim WithEvents ExtractPKGMenuItem As New Controls.MenuItem() With {.Header = "Extract .pkg", .Icon = New Controls.Image() With {.Source = New BitmapImage(New Uri("/Images/extract.png", UriKind.Relative))}}
    Dim WithEvents PlayMenuItem As New Controls.MenuItem() With {.Header = "Play Soundtrack", .Icon = New Controls.Image() With {.Source = New BitmapImage(New Uri("/Images/Play-icon.png", UriKind.Relative))}}
    Dim WithEvents PKGInfoMenuItem As New Controls.MenuItem() With {.Header = "PKG Details", .Icon = New Controls.Image() With {.Source = New BitmapImage(New Uri("/Images/information-button.png", UriKind.Relative))}}

    'ISO tools context menu items
    Dim WithEvents ISOToolsMenuItem As New Controls.MenuItem() With {.Header = "ISO Tools", .Icon = New Controls.Image() With {.Source = New BitmapImage(New Uri("/Images/iso.png", UriKind.Relative))}}
    Dim WithEvents ExtractISOMenuItem As New Controls.MenuItem() With {.Header = "Extract ISO", .Icon = New Controls.Image() With {.Source = New BitmapImage(New Uri("/Images/extract.png", UriKind.Relative))}}
    Dim WithEvents CreateISOMenuItem As New Controls.MenuItem() With {.Header = "Create ISO", .Icon = New Controls.Image() With {.Source = New BitmapImage(New Uri("/Images/create.png", UriKind.Relative))}}
    Dim WithEvents PatchISOMenuItem As New Controls.MenuItem() With {.Header = "Patch ISO", .Icon = New Controls.Image() With {.Source = New BitmapImage(New Uri("/Images/patch.png", UriKind.Relative))}}
    Dim WithEvents SplitISOMenuItem As New Controls.MenuItem() With {.Header = "Split ISO", .Icon = New Controls.Image() With {.Source = New BitmapImage(New Uri("/Images/split.png", UriKind.Relative))}}

    'webMAN MOD ISO utilities context menu items
    Dim WithEvents MountISOMenuItem As New Controls.MenuItem() With {.Header = "Mount selected game", .Icon = New Controls.Image() With {.Source = New BitmapImage(New Uri("/Images/iso.png", UriKind.Relative))}}
    Dim WithEvents MountAndPlayISOMenuItem As New Controls.MenuItem() With {.Header = "Mount & Play selected game", .Icon = New Controls.Image() With {.Source = New BitmapImage(New Uri("/Images/iso.png", UriKind.Relative))}}

    'Supplemental library menu items
    Dim WithEvents LoadFolderMenuItem As New Controls.MenuItem() With {.Header = "Load a local backup folder"}
    Dim WithEvents LoadRemoteFolderMenuItem As New Controls.MenuItem() With {.Header = "Load games on PS3 HDD"}
    Dim WithEvents LoadDLFolderMenuItem As New Controls.MenuItem() With {.Header = "Open Downloads folder"}

    Private Sub PS3Library_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        'Add supplemental library menu items that will be handled in the app
        Dim LibraryMenuItem As Controls.MenuItem = CType(NewPS3Menu.Items(1), Controls.MenuItem)
        LibraryMenuItem.Items.Add(LoadFolderMenuItem)
        LibraryMenuItem.Items.Add(LoadRemoteFolderMenuItem)
        LibraryMenuItem.Items.Add(LoadDLFolderMenuItem)

        'Add the new PKG Browser
        Dim PKGDownloaderMenuItem As New MenuItem() With {.Header = "PKG Browser & Downloader"}
        AddHandler PKGDownloaderMenuItem.Click, AddressOf OpenPKGBrowser
        NewPS3Menu.Items.Add(PKGDownloaderMenuItem)

        'Load available context menu options
        PS3GamesListView.ContextMenu = NewContextMenu
    End Sub

    Private Sub PS3Library_ContentRendered(sender As Object, e As EventArgs) Handles Me.ContentRendered
        'Load config if exists
        If File.Exists(My.Computer.FileSystem.CurrentDirectory + "\psmt-config.ini") Then
            Try
                Dim MainConfig As New IniFile(My.Computer.FileSystem.CurrentDirectory + "\psmt-config.ini")
                ConsoleIP = MainConfig.IniReadValue("PS3 Tools", "IP")
                ConsolePort = MainConfig.IniReadValue("PS3 Tools", "Port")
            Catch ex As FileNotFoundException
                MsgBox("Could not find a valid config file.", MsgBoxStyle.Exclamation)
            End Try
        End If
    End Sub

    Private Sub NewPS3Menu_IPTextChanged(sender As Object, e As RoutedEventArgs) Handles NewPS3Menu.IPTextChanged
        ConsoleIP = NewPS3Menu.SharedConsoleAddress.Split(":"c)(0)
        ConsolePort = NewPS3Menu.SharedConsoleAddress.Split(":"c)(1)

        'Save config
        Try
            Dim MainConfig As New IniFile(My.Computer.FileSystem.CurrentDirectory + "\psmt-config.ini")
            MainConfig.IniWriteValue("PS3 Tools", "IP", NewPS3Menu.SharedConsoleAddress.Split(":"c)(0))
            MainConfig.IniWriteValue("PS3 Tools", "Port", NewPS3Menu.SharedConsoleAddress.Split(":"c)(1))
        Catch ex As FileNotFoundException
            MsgBox("Could not find a valid config file.", MsgBoxStyle.Exclamation)
        End Try
    End Sub

#Region "Game Loader"

    Public Enum LoadType
        BackupFolder
        FTP
    End Enum

    Public Structure GameLoaderArgs
        Private _Type As LoadType
        Private _FolderPath As String
        Private _ConsoleIP As String

        Public Property Type As LoadType
            Get
                Return _Type
            End Get
            Set
                _Type = Value
            End Set
        End Property

        Public Property FolderPath As String
            Get
                Return _FolderPath
            End Get
            Set
                _FolderPath = Value
            End Set
        End Property

        Public Property ConsoleIP As String
            Get
                Return _ConsoleIP
            End Get
            Set
                _ConsoleIP = Value
            End Set
        End Property
    End Structure

    Private Sub GameLoaderWorker_DoWork(sender As Object, e As DoWorkEventArgs) Handles GameLoaderWorker.DoWork
        Dim WorkerArgs As GameLoaderArgs = CType(e.Argument, GameLoaderArgs)

        If WorkerArgs.Type = LoadType.FTP Then
            Try
                Using conn As New FtpClient(WorkerArgs.ConsoleIP, "anonymous", "anonymous", 21)
                    'Configurate the FTP connection
                    conn.Config.ValidateAnyCertificate = True
                    conn.Config.SslProtocols = SslProtocols.None
                    conn.Config.DataConnectionEncryption = False
                    conn.Config.DataConnectionType = FtpDataConnectionType.PASV

                    'Connect
                    conn.Connect()

                    'Get /dev_hdd0/game
                    If conn.DirectoryExists("/dev_hdd0/game") Then
                        For Each item In conn.GetListing("/dev_hdd0/game")

                            Dim NewPS3Game As New PS3Game()

                        Next
                    End If

                    'Get /dev_hdd0/GAMES
                    If conn.DirectoryExists("/dev_hdd0/GAMES") Then
                        For Each item In conn.GetListing("/dev_hdd0/GAMES")

                            Dim NewPS3Game As New PS3Game()

                            If item.Type = FtpObjectType.Directory Then
                                If conn.DirectoryExists(item.FullName + "/PS3_GAME") Then

                                    If conn.FileExists(item.FullName + "/PS3_GAME/PARAM.SFO") Then

                                    End If

                                End If

                            End If
                        Next
                    End If

                    'Get PS3ISO games
                    If conn.DirectoryExists("/dev_hdd0/PS3ISO") Then
                        For Each item In conn.GetListing("/dev_hdd0/PS3ISO")
                            If item.Type = FtpObjectType.File Then
                                If item.Name.EndsWith(".iso") Then

                                    'Dim ISOCacheFolderName As String = Path.GetFileNameWithoutExtension(item.FullName)
                                    'Dim FullFTPPath As String = "ftp://" + WorkerArgs.ConsoleIP + item.FullName

                                    Dim NewPS3Game As New PS3Game With {
                                        .GridWidth = 210,
                                        .GridHeight = 210,
                                        .ImageWidth = 200,
                                        .ImageHeight = 200,
                                        .GameSize = FormatNumber(item.Size / 1073741824, 2) + " GB",
                                        .GameFilePath = item.FullName,
                                        .GameFileType = PS3Game.GameFileTypes.PS3ISO,
                                        .GameRootLocation = PS3Game.GameLocation.WebMANMOD,
                                        .GameTitle = item.Name
                                    }

                                    GamesList.Add(NewPS3Game)

                                    If Dispatcher.CheckAccess() = False Then
                                        Dispatcher.BeginInvoke(Sub() NewPS3Game.GameCoverSource = New BitmapImage(New Uri("/Images/PS3Disc.png", UriKind.RelativeOrAbsolute)))
                                    Else
                                        NewPS3Game.GameCoverSource = New BitmapImage(New Uri("/Images/PS3Disc.png", UriKind.RelativeOrAbsolute))
                                    End If

                                    'Add to the ListView
                                    If PS3GamesListView.Dispatcher.CheckAccess() = False Then
                                        PS3GamesListView.Dispatcher.BeginInvoke(Sub() PS3GamesListView.Items.Add(NewPS3Game))
                                    Else
                                        PS3GamesListView.Items.Add(NewPS3Game)
                                    End If

                                End If
                            End If
                        Next
                    End If

                    'Get PS2ISO games
                    If conn.DirectoryExists("/dev_hdd0/PS2ISO") Then
                        For Each item In conn.GetListing("/dev_hdd0/PS2ISO")
                            If item.Type = FtpObjectType.File Then
                                If item.Name.EndsWith(".bin.enc") Then

                                    Dim NewPS3Game As New PS3Game With {
                                        .GridWidth = 210,
                                        .GridHeight = 210,
                                        .ImageWidth = 200,
                                        .ImageHeight = 200,
                                        .GameSize = FormatNumber(item.Size / 1073741824, 2) + " GB",
                                        .GameFilePath = item.FullName,
                                        .GameFileType = PS3Game.GameFileTypes.PS2ISO,
                                        .GameRootLocation = PS3Game.GameLocation.WebMANMOD,
                                        .GameTitle = item.Name
                                    }

                                    GamesList.Add(NewPS3Game)

                                    If Dispatcher.CheckAccess() = False Then
                                        Dispatcher.BeginInvoke(Sub() NewPS3Game.GameCoverSource = New BitmapImage(New Uri("/Images/PS2Disc.png", UriKind.RelativeOrAbsolute)))
                                    Else
                                        NewPS3Game.GameCoverSource = New BitmapImage(New Uri("/Images/PS2Disc.png", UriKind.RelativeOrAbsolute))
                                    End If

                                    'Add to the ListView
                                    If PS3GamesListView.Dispatcher.CheckAccess() = False Then
                                        PS3GamesListView.Dispatcher.BeginInvoke(Sub() PS3GamesListView.Items.Add(NewPS3Game))
                                    Else
                                        PS3GamesListView.Items.Add(NewPS3Game)
                                    End If

                                End If
                            End If
                        Next
                    End If

                    'Get PSXISO games
                    If conn.DirectoryExists("/dev_hdd0/PSXISO") Then
                        For Each item In conn.GetListing("/dev_hdd0/PSXISO")
                            If item.Type = FtpObjectType.File Then
                                If item.Name.EndsWith(".bin") Then

                                    Dim NewPS3Game As New PS3Game With {
                                        .GridWidth = 210,
                                        .GridHeight = 210,
                                        .ImageWidth = 200,
                                        .ImageHeight = 200,
                                        .GameSize = FormatNumber(item.Size / 1073741824, 2) + " GB",
                                        .GameFilePath = item.FullName,
                                        .GameFileType = PS3Game.GameFileTypes.PSXISO,
                                        .GameRootLocation = PS3Game.GameLocation.WebMANMOD,
                                        .GameTitle = item.Name
                                    }

                                    GamesList.Add(NewPS3Game)

                                    If Dispatcher.CheckAccess() = False Then
                                        Dispatcher.BeginInvoke(Sub() NewPS3Game.GameCoverSource = New BitmapImage(New Uri("/Images/PS1Disc.png", UriKind.RelativeOrAbsolute)))
                                    Else
                                        NewPS3Game.GameCoverSource = New BitmapImage(New Uri("/Images/PS1Disc.png", UriKind.RelativeOrAbsolute))
                                    End If

                                    'Add to the ListView
                                    If PS3GamesListView.Dispatcher.CheckAccess() = False Then
                                        PS3GamesListView.Dispatcher.BeginInvoke(Sub() PS3GamesListView.Items.Add(NewPS3Game))
                                    Else
                                        PS3GamesListView.Items.Add(NewPS3Game)
                                    End If

                                End If
                            End If
                        Next
                    End If

                    'Get PSPISO games
                    If conn.DirectoryExists("/dev_hdd0/PSPISO") Then
                        For Each item In conn.GetListing("/dev_hdd0/PSPISO")
                            If item.Type = FtpObjectType.File Then
                                If item.Name.EndsWith(".iso") Then

                                    Dim NewPS3Game As New PS3Game With {
                                        .GridWidth = 210,
                                        .GridHeight = 210,
                                        .ImageWidth = 200,
                                        .ImageHeight = 200,
                                        .GameSize = FormatNumber(item.Size / 1073741824, 2) + " GB",
                                        .GameFilePath = item.FullName,
                                        .GameFileType = PS3Game.GameFileTypes.PSPISO,
                                        .GameRootLocation = PS3Game.GameLocation.WebMANMOD,
                                        .GameTitle = item.Name
                                    }

                                    GamesList.Add(NewPS3Game)

                                    If Dispatcher.CheckAccess() = False Then
                                        Dispatcher.BeginInvoke(Sub() NewPS3Game.GameCoverSource = New BitmapImage(New Uri("/Images/UMD.png", UriKind.RelativeOrAbsolute)))
                                    Else
                                        NewPS3Game.GameCoverSource = New BitmapImage(New Uri("/Images/UMD.png", UriKind.RelativeOrAbsolute))
                                    End If

                                    'Add to the ListView
                                    If PS3GamesListView.Dispatcher.CheckAccess() = False Then
                                        PS3GamesListView.Dispatcher.BeginInvoke(Sub() PS3GamesListView.Items.Add(NewPS3Game))
                                    Else
                                        PS3GamesListView.Items.Add(NewPS3Game)
                                    End If

                                End If
                            End If
                        Next
                    End If

                    'Disconnect
                    conn.Disconnect()
                End Using
            Catch ex As Exception
                MsgBox(ex.ToString())
            End Try
        ElseIf WorkerArgs.Type = LoadType.BackupFolder Then
            'PS3 classic backup folders
            For Each Game In Directory.GetFiles(WorkerArgs.FolderPath, "*.SFO", SearchOption.AllDirectories)

                Dim NewPS3Game As New PS3Game() With {.GridWidth = 325, .GridHeight = 180, .ImageWidth = 320, .ImageHeight = 176}

                Using SFOReader As New Process()
                    SFOReader.StartInfo.FileName = My.Computer.FileSystem.CurrentDirectory + "\Tools\sfo.exe"
                    SFOReader.StartInfo.Arguments = """" + Game + """ --decimal"
                    SFOReader.StartInfo.RedirectStandardOutput = True
                    SFOReader.StartInfo.UseShellExecute = False
                    SFOReader.StartInfo.CreateNoWindow = True
                    SFOReader.Start()

                    Dim OutputReader As StreamReader = SFOReader.StandardOutput
                    Dim ProcessOutput As String() = OutputReader.ReadToEnd().Split(New String() {vbCrLf}, StringSplitOptions.RemoveEmptyEntries)

                    If ProcessOutput.Count > 0 Then

                        'Load game infos
                        For Each Line In ProcessOutput
                            If Line.StartsWith("TITLE=") Then
                                NewPS3Game.GameTitle = Utils.CleanTitle(Line.Split("="c)(1).Trim(""""c))
                            ElseIf Line.StartsWith("TITLE_ID=") Then
                                NewPS3Game.GameID = Line.Split("="c)(1).Trim(""""c)
                            ElseIf Line.StartsWith("CATEGORY=") Then
                                NewPS3Game.GameCategory = PS3Game.GetCategory(Line.Split("="c)(1).Trim(""""c))
                            ElseIf Line.StartsWith("APP_VER=") Then
                                NewPS3Game.GameAppVer = FormatNumber(Line.Split("="c)(1).Trim(""""c), 2)
                            ElseIf Line.StartsWith("PS3_SYSTEM_VER=") Then
                                NewPS3Game.GameRequiredFW = FormatNumber(Line.Split("="c)(1).Trim(""""c), 2)
                            ElseIf Line.StartsWith("VERSION=") Then
                                NewPS3Game.GameVer = "Version: " + FormatNumber(Line.Split("="c)(1).Trim(""""c), 2)
                            ElseIf Line.StartsWith("RESOLUTION=") Then
                                NewPS3Game.GameResolution = PS3Game.GetGameResolution(Line.Split("="c)(1).Trim(""""c))
                            ElseIf Line.StartsWith("SOUND_FORMAT=") Then
                                NewPS3Game.GameSoundFormat = PS3Game.GetGameSoundFormat(Line.Split("="c)(1).Trim(""""c))
                            End If
                        Next

                        'Load game files
                        Dim PS3GAMEFolder As String = Path.GetDirectoryName(Game)
                        If File.Exists(PS3GAMEFolder + "\ICON0.PNG") Then
                            If Dispatcher.CheckAccess() = False Then
                                Dispatcher.BeginInvoke(Sub()
                                                           Dim TempBitmapImage = New BitmapImage()
                                                           TempBitmapImage.BeginInit()
                                                           TempBitmapImage.CacheOption = BitmapCacheOption.OnLoad
                                                           TempBitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache
                                                           TempBitmapImage.UriSource = New Uri(PS3GAMEFolder + "\ICON0.PNG", UriKind.RelativeOrAbsolute)
                                                           TempBitmapImage.EndInit()
                                                           NewPS3Game.GameCoverSource = TempBitmapImage
                                                       End Sub)
                            Else
                                Dim TempBitmapImage = New BitmapImage()
                                TempBitmapImage.BeginInit()
                                TempBitmapImage.CacheOption = BitmapCacheOption.OnLoad
                                TempBitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache
                                TempBitmapImage.UriSource = New Uri(PS3GAMEFolder + "\ICON0.PNG", UriKind.RelativeOrAbsolute)
                                TempBitmapImage.EndInit()
                                NewPS3Game.GameCoverSource = TempBitmapImage
                            End If
                        End If
                        If File.Exists(PS3GAMEFolder + "\PIC1.PNG") Then
                            If Dispatcher.CheckAccess() = False Then
                                Dispatcher.BeginInvoke(Sub()
                                                           Dim TempBitmapImage = New BitmapImage()
                                                           TempBitmapImage.BeginInit()
                                                           TempBitmapImage.CacheOption = BitmapCacheOption.OnLoad
                                                           TempBitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache
                                                           TempBitmapImage.UriSource = New Uri(PS3GAMEFolder + "\PIC1.PNG", UriKind.RelativeOrAbsolute)
                                                           TempBitmapImage.EndInit()
                                                           NewPS3Game.GameBackgroundSource = TempBitmapImage
                                                       End Sub)
                            Else
                                Dim TempBitmapImage = New BitmapImage()
                                TempBitmapImage.BeginInit()
                                TempBitmapImage.CacheOption = BitmapCacheOption.OnLoad
                                TempBitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache
                                TempBitmapImage.UriSource = New Uri(PS3GAMEFolder + "\PIC1.PNG", UriKind.RelativeOrAbsolute)
                                TempBitmapImage.EndInit()
                                NewPS3Game.GameBackgroundSource = TempBitmapImage
                            End If
                        End If
                        If File.Exists(PS3GAMEFolder + "\SND0.AT3") Then
                            NewPS3Game.GameBackgroundSoundFile = PS3GAMEFolder + "\SND0.AT3"
                        End If

                        Dim PS3GAMEFolderSize As Long = Utils.DirSize(PS3GAMEFolder, True)
                        NewPS3Game.GameSize = FormatNumber(PS3GAMEFolderSize / 1073741824, 2) + " GB"
                        NewPS3Game.GameFolderPath = Directory.GetParent(PS3GAMEFolder).FullName

                        NewPS3Game.GameFileType = PS3Game.GameFileTypes.Backup
                        NewPS3Game.GameRootLocation = PS3Game.GameLocation.Local

                        If Not String.IsNullOrWhiteSpace(NewPS3Game.GameID) Then
                            NewPS3Game.GameRegion = PS3Game.GetGameRegion(NewPS3Game.GameID)
                        End If

                        'Update progress
                        Dispatcher.BeginInvoke(Sub() NewLoadingWindow.LoadProgressBar.Value += 1)
                        Dispatcher.BeginInvoke(Sub() NewLoadingWindow.LoadStatusTextBlock.Text = "Loading folder " + (NewLoadingWindow.LoadProgressBar.Value - ISOCount - PKGCount).ToString + " of " + FoldersCount.ToString())

                        GamesList.Add(NewPS3Game)

                        'Add to the ListView
                        If PS3GamesListView.Dispatcher.CheckAccess() = False Then
                            PS3GamesListView.Dispatcher.BeginInvoke(Sub() PS3GamesListView.Items.Add(NewPS3Game))
                        Else
                            PS3GamesListView.Items.Add(NewPS3Game)
                        End If

                    End If

                End Using

            Next

            'PS3 PKGs
            For Each GamePKG In Directory.GetFiles(WorkerArgs.FolderPath, "*.pkg", SearchOption.AllDirectories)

                Dim NewPS3Game As New PS3Game() With {.GridWidth = 325, .GridHeight = 180, .ImageWidth = 320, .ImageHeight = 176}
                Dim PKGFileInfo As New FileInfo(GamePKG)
                Dim NewPKGDecryptor As New PKGDecryptor()

                Try
                    'Decrypt pkg file
                    NewPKGDecryptor.ProcessPKGFile(GamePKG)

                    'Load game infos
                    If NewPKGDecryptor.GetPARAMSFO IsNot Nothing Then
                        Dim SFOKeys As Dictionary(Of String, Object) = SFONew.ReadSfo(NewPKGDecryptor.GetPARAMSFO)
                        If SFOKeys.ContainsKey("TITLE") Then
                            NewPS3Game.GameTitle = Utils.CleanTitle(SFOKeys("TITLE").ToString)
                        End If
                        If SFOKeys.ContainsKey("TITLE_ID") Then
                            NewPS3Game.GameID = SFOKeys("TITLE_ID").ToString
                        End If
                        If SFOKeys.ContainsKey("CATEGORY") Then
                            NewPS3Game.GameCategory = PS3Game.GetCategory(SFOKeys("CATEGORY").ToString)
                        End If
                        If SFOKeys.ContainsKey("CONTENT_ID") Then
                            NewPS3Game.ContentID = SFOKeys("CONTENT_ID").ToString
                        End If
                        If SFOKeys.ContainsKey("APP_VER") Then
                            Dim AppVer As String = SFOKeys("APP_VER").ToString().Substring(0, 5)
                            NewPS3Game.GameAppVer = AppVer
                        End If
                        If SFOKeys.ContainsKey("PS3_SYSTEM_VER") Then
                            Dim SystemVer As String = SFOKeys("PS3_SYSTEM_VER").ToString().Substring(0, 5)
                            NewPS3Game.GameRequiredFW = SystemVer
                        End If
                        If SFOKeys.ContainsKey("VERSION") Then
                            Dim Ver As String = SFOKeys("VERSION").ToString().Substring(0, 5)
                            NewPS3Game.GameVer = Ver
                        End If
                        If SFOKeys.ContainsKey("RESOLUTION") Then
                            NewPS3Game.GameResolution = PS3Game.GetGameResolution(SFOKeys("RESOLUTION").ToString)
                        End If
                        If SFOKeys.ContainsKey("SOUND_FORMAT") Then
                            NewPS3Game.GameSoundFormat = PS3Game.GetGameSoundFormat(SFOKeys("SOUND_FORMAT").ToString)
                        End If
                    End If

                    NewPS3Game.GameSize = FormatNumber(PKGFileInfo.Length / 1073741824, 2) + " GB"
                    NewPS3Game.GameFileType = PS3Game.GameFileTypes.PKG
                    NewPS3Game.GameRootLocation = PS3Game.GameLocation.Local

                    If Not String.IsNullOrWhiteSpace(NewPS3Game.GameID) Then
                        NewPS3Game.GameRegion = PS3Game.GetGameRegion(NewPS3Game.GameID)
                    End If

                    NewPS3Game.GameFilePath = GamePKG

                    'Check for additional content
                    If NewPKGDecryptor.ICON0 IsNot Nothing Then
                        If Dispatcher.CheckAccess = False Then
                            Dispatcher.BeginInvoke(Sub() NewPS3Game.GameCoverSource = NewPKGDecryptor.ICON0)
                        Else
                            NewPS3Game.GameCoverSource = NewPKGDecryptor.ICON0
                        End If
                    End If
                    If NewPKGDecryptor.PIC1 IsNot Nothing Then
                        If Dispatcher.CheckAccess = False Then
                            Dispatcher.BeginInvoke(Sub() NewPS3Game.GameBackgroundSource = NewPKGDecryptor.PIC1)
                        Else
                            NewPS3Game.GameBackgroundSource = NewPKGDecryptor.PIC1
                        End If
                    End If
                    If NewPKGDecryptor.SND0 IsNot Nothing Then
                        If Dispatcher.CheckAccess = False Then
                            Dispatcher.BeginInvoke(Sub() NewPS3Game.GameBackgroundSoundBytes = NewPKGDecryptor.SND0)
                        Else
                            NewPS3Game.GameBackgroundSoundBytes = NewPKGDecryptor.SND0
                        End If
                    End If

                    'Update progress
                    Dispatcher.BeginInvoke(Sub() NewLoadingWindow.LoadProgressBar.Value += 1)
                    Dispatcher.BeginInvoke(Sub() NewLoadingWindow.LoadStatusTextBlock.Text = "Loading PKG " + (NewLoadingWindow.LoadProgressBar.Value - ISOCount - FoldersCount).ToString + " of " + PKGCount.ToString())

                    GamesList.Add(NewPS3Game)

                    'Add to the ListView
                    If PS3GamesListView.Dispatcher.CheckAccess() = False Then
                        PS3GamesListView.Dispatcher.BeginInvoke(Sub() PS3GamesListView.Items.Add(NewPS3Game))
                    Else
                        PS3GamesListView.Items.Add(NewPS3Game)
                    End If

                Catch ex As Exception
                    If NewPKGDecryptor.ContentID IsNot Nothing Then
                        NewPS3Game.GameTitle = NewPKGDecryptor.ContentID
                        NewPS3Game.GameID = "ID: " + Utils.GetPKGTitleID(GamePKG)
                    Else
                        NewPS3Game.GameTitle = "Unsupported PS3 .pkg"
                    End If
                    Continue For
                End Try

            Next

            'PS3 ISOs
            For Each GameISO In Directory.GetFiles(WorkerArgs.FolderPath, "*.iso", SearchOption.AllDirectories)

                Dim NewPS3Game As New PS3Game() With {.GridWidth = 325, .GridHeight = 180, .ImageWidth = 320, .ImageHeight = 176}
                Dim ISOFileInfo As New FileInfo(GameISO)
                Dim ISOCacheFolderName As String = Path.GetFileNameWithoutExtension(ISOFileInfo.Name)

                'Create cache dir for PS3 games
                If Not Directory.Exists(My.Computer.FileSystem.CurrentDirectory + "\Cache\PS3\" + ISOCacheFolderName) Then
                    Directory.CreateDirectory(My.Computer.FileSystem.CurrentDirectory + "\Cache\PS3\" + ISOCacheFolderName)
                End If

                'Extract files to display infos
                If Not File.Exists(My.Computer.FileSystem.CurrentDirectory + "\Cache\PS3\" + ISOCacheFolderName + "\PARAM.SFO") Then
                    Using ISOExtractor As New Process()
                        ISOExtractor.StartInfo.FileName = My.Computer.FileSystem.CurrentDirectory + "\Tools\7z.exe"
                        ISOExtractor.StartInfo.Arguments = "e """ + GameISO + """" +
                            " -o""" + My.Computer.FileSystem.CurrentDirectory + "\Cache\PS3\" + ISOCacheFolderName + """" +
                            " PS3_GAME/PARAM.SFO PARAM.SFO PS3_GAME/ICON0.PNG ICON0.PNG PS3_GAME/PIC1.PNG PIC1.PNG PS3_GAME/SND0.AT3 SND0.AT3"
                        ISOExtractor.StartInfo.RedirectStandardOutput = True
                        ISOExtractor.StartInfo.UseShellExecute = False
                        ISOExtractor.StartInfo.CreateNoWindow = True
                        ISOExtractor.Start()
                        ISOExtractor.WaitForExit()
                    End Using
                End If

                Using ParamFileStream As New FileStream(My.Computer.FileSystem.CurrentDirectory + "\Cache\PS3\" + ISOCacheFolderName + "\PARAM.SFO", FileMode.Open, FileAccess.Read)
                    Dim SFOKeys As Dictionary(Of String, Object) = SFONew.ReadSfo(ParamFileStream)
                    If SFOKeys IsNot Nothing AndAlso SFOKeys.Count > 0 Then
                        If SFOKeys.ContainsKey("APP_VER") Then
                            Dim AppVer As String = SFOKeys("APP_VER").ToString().Substring(0, 5)
                            NewPS3Game.GameAppVer = AppVer
                        End If
                        If SFOKeys.ContainsKey("CATEGORY") Then
                            NewPS3Game.GameCategory = PS3Game.GetCategory(SFOKeys("CATEGORY").ToString)
                        End If
                        If SFOKeys.ContainsKey("CONTENT_ID") Then
                            NewPS3Game.ContentID = SFOKeys("CONTENT_ID").ToString
                        End If
                        If SFOKeys.ContainsKey("PS3_SYSTEM_VER") Then
                            Dim SystemVer As String = SFOKeys("PS3_SYSTEM_VER").ToString().Substring(0, 5)
                            NewPS3Game.GameRequiredFW = SystemVer
                        End If
                        If SFOKeys.ContainsKey("RESOLUTION") Then
                            NewPS3Game.GameResolution = PS3Game.GetGameResolution(SFOKeys("RESOLUTION").ToString)
                        End If
                        If SFOKeys.ContainsKey("SOUND_FORMAT") Then
                            NewPS3Game.GameSoundFormat = PS3Game.GetGameSoundFormat(SFOKeys("SOUND_FORMAT").ToString)
                        End If
                        If SFOKeys.ContainsKey("TITLE") Then
                            NewPS3Game.GameTitle = Utils.CleanTitle(SFOKeys("TITLE").ToString)
                        End If
                        If SFOKeys.ContainsKey("TITLE_ID") Then
                            NewPS3Game.GameID = SFOKeys("TITLE_ID").ToString
                        End If
                        If SFOKeys.ContainsKey("VERSION") Then
                            Dim Ver As String = SFOKeys("VERSION").ToString().Substring(0, 5)
                            NewPS3Game.GameVer = Ver
                        End If
                    End If
                End Using

                'Load game files
                Dim PS3GAMEFolder As String = My.Computer.FileSystem.CurrentDirectory + "\Cache\PS3\" + ISOCacheFolderName

                If File.Exists(PS3GAMEFolder + "\ICON0.PNG") Then
                    Dispatcher.BeginInvoke(Sub() NewPS3Game.GameCoverSource = New BitmapImage(New Uri(PS3GAMEFolder + "\ICON0.PNG", UriKind.RelativeOrAbsolute)))
                End If
                If File.Exists(PS3GAMEFolder + "\PIC1.PNG") Then
                    Dispatcher.BeginInvoke(Sub() NewPS3Game.GameBackgroundPath = PS3GAMEFolder + "\PIC1.PNG")
                End If
                If File.Exists(PS3GAMEFolder + "\SND0.AT3") Then
                    NewPS3Game.GameBackgroundSoundFile = PS3GAMEFolder + "\SND0.AT3"
                End If

                NewPS3Game.GameSize = FormatNumber(ISOFileInfo.Length / 1073741824, 2) + " GB"

                If Not String.IsNullOrWhiteSpace(NewPS3Game.GameID) Then
                    NewPS3Game.GameRegion = PS3Game.GetGameRegion(NewPS3Game.GameID)
                End If

                NewPS3Game.GameFilePath = GameISO
                NewPS3Game.GameFileType = PS3Game.GameFileTypes.PS3ISO
                NewPS3Game.GameRootLocation = PS3Game.GameLocation.Local

                'Update progress
                Dispatcher.BeginInvoke(Sub() NewLoadingWindow.LoadProgressBar.Value += 1)
                Dispatcher.BeginInvoke(Sub() NewLoadingWindow.LoadStatusTextBlock.Text = "Loading ISO " + (NewLoadingWindow.LoadProgressBar.Value - FoldersCount - PKGCount).ToString + " of " + ISOCount.ToString())

                GamesList.Add(NewPS3Game)

                'Add to the ListView
                If PS3GamesListView.Dispatcher.CheckAccess() = False Then
                    PS3GamesListView.Dispatcher.BeginInvoke(Sub() PS3GamesListView.Items.Add(NewPS3Game))
                Else
                    PS3GamesListView.Items.Add(NewPS3Game)
                End If

            Next
        End If
    End Sub

    Private Sub GameLoaderWorker_RunWorkerCompleted(sender As Object, e As RunWorkerCompletedEventArgs) Handles GameLoaderWorker.RunWorkerCompleted
        NewLoadingWindow.Close()
    End Sub

#End Region

#Region "Contextmenu General ISO Tools"

    Private Sub ExtractISOMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles ExtractISOMenuItem.Click
        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedGame As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)
            If File.Exists(SelectedGame.GameFolderPath) Then
                Dim NewISOTools As New PS3ISOTools With {.ShowActivated = True, .ISOToExtract = SelectedGame.GameFolderPath}
                NewISOTools.Show()
                MsgBox("Please continue with PS3 ISO Tools and specify an output folder.", MsgBoxStyle.Information)
            End If
        End If
    End Sub

    Private Sub CreateISOMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles CreateISOMenuItem.Click
        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedGame As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)
            If Directory.Exists(SelectedGame.GameFolderPath) Then
                Dim NewISOTools As New PS3ISOTools With {.ShowActivated = True, .ISOToCreate = SelectedGame.GameFolderPath}
                NewISOTools.Show()
                MsgBox("Please continue with PS3 ISO Tools and specify an output folder.", MsgBoxStyle.Information)
            End If
        End If
    End Sub

    Private Sub PatchISOMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles PatchISOMenuItem.Click
        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedGame As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)
            If File.Exists(SelectedGame.GameFolderPath) Then
                Dim NewISOTools As New PS3ISOTools With {.ShowActivated = True, .ISOToPatch = SelectedGame.GameFolderPath}
                NewISOTools.Show()
                MsgBox("Please continue with PS3 ISO Tools and specify an output folder.", MsgBoxStyle.Information)
            End If
        End If
    End Sub

    Private Sub SplitISOMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles SplitISOMenuItem.Click
        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedGame As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)
            If File.Exists(SelectedGame.GameFolderPath) Then
                Dim NewISOTools As New PS3ISOTools With {.ShowActivated = True, .ISOToSplit = SelectedGame.GameFolderPath}
                NewISOTools.Show()
                MsgBox("Please continue with PS3 ISO Tools and specify an output folder.", MsgBoxStyle.Information)
            End If
        End If
    End Sub

#End Region

#Region "Contextmenu webMAN MOD ISO Tools"

    Private Sub MountISOMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles MountISOMenuItem.Click
        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedGame As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)
            If Not String.IsNullOrEmpty(ConsoleIP) Then
                If Not String.IsNullOrEmpty(SelectedGame.GameFilePath) AndAlso SelectedGame.GameRootLocation = PS3Game.GameLocation.WebMANMOD Then
                    NewPS3Menu.NavigateTowebMANMODUrl("http://" & ConsoleIP & "/mount.ps3" + SelectedGame.GameFilePath)
                End If
            Else
                MsgBox("Please set your PS3 IP address in the Settings.", MsgBoxStyle.Information, "No IP Address")
            End If
        End If
    End Sub

    Private Sub MountAndPlayISOMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles MountAndPlayISOMenuItem.Click
        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedGame As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)
            If Not String.IsNullOrEmpty(ConsoleIP) Then
                If Not String.IsNullOrEmpty(SelectedGame.GameFilePath) AndAlso SelectedGame.GameRootLocation = PS3Game.GameLocation.WebMANMOD Then
                    NewPS3Menu.NavigateTowebMANMODUrl("http://" & ConsoleIP & "/play.ps3" + SelectedGame.GameFilePath)
                End If
            Else
                MsgBox("Please set your PS3 IP address in the Settings.", MsgBoxStyle.Information, "No IP Address")
            End If
        End If
    End Sub

#End Region

#Region "Games Library Contextmenu Actions"

    Private Sub ExtractPKGMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles ExtractPKGMenuItem.Click
        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedPS3Game As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)
            Dim NewPKGExtractor As New PS3PKGExtractor() With {.SelectedPKG = SelectedPS3Game.GameFilePath}
            NewPKGExtractor.Show()
        End If
    End Sub

    Private Sub PlayMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles PlayMenuItem.Click
        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedPS3Game As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)

            If SelectedPS3Game.GameBackgroundSoundFile IsNot Nothing Then
                If IsSoundPlaying Then
                    Utils.StopGameSound()
                    IsSoundPlaying = False
                Else
                    Utils.PlayGameSound(SelectedPS3Game.GameBackgroundSoundFile)
                    IsSoundPlaying = True
                End If
            ElseIf SelectedPS3Game.GameBackgroundSoundBytes IsNot Nothing Then
                If IsSoundPlaying Then
                    Utils.StopSND()
                    IsSoundPlaying = False
                Else
                    Utils.PlaySND(SelectedPS3Game.GameBackgroundSoundBytes)
                    IsSoundPlaying = True
                End If
            Else
                If IsSoundPlaying Then
                    Utils.StopGameSound()
                    IsSoundPlaying = False
                Else
                    MsgBox("No game soundtrack found.", MsgBoxStyle.Information)
                End If
            End If
        End If
    End Sub

    Private Sub PKGInfoMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles PKGInfoMenuItem.Click
        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedPS3Game As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)
            Dim NewPKGInfo As New PKGInfo() With {.SelectedPKG = SelectedPS3Game.GameFilePath, .Console = "PS3"}
            NewPKGInfo.Show()
        End If
    End Sub

    Private Sub CopyToMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles CopyToMenuItem.Click
        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedPS3Game As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)
            Dim FBD As New Forms.FolderBrowserDialog() With {.Description = "Where do you want to save the selected game ?"}

            If FBD.ShowDialog() = Forms.DialogResult.OK Then
                Dim NewCopyWindow As New CopyWindow() With {.ShowActivated = True,
                    .WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    .BackupDestinationPath = FBD.SelectedPath + "\",
                    .Title = "Copying " + SelectedPS3Game.GameTitle + " to " + FBD.SelectedPath + "\" + Path.GetFileName(SelectedPS3Game.GameFilePath)}

                If SelectedPS3Game.GameFileType = PS3Game.GameFileTypes.Backup Then
                    NewCopyWindow.BackupPath = SelectedPS3Game.GameFolderPath
                ElseIf SelectedPS3Game.GameFileType = PS3Game.GameFileTypes.PS3ISO Then
                    NewCopyWindow.BackupPath = SelectedPS3Game.GameFilePath
                ElseIf SelectedPS3Game.GameFileType = PS3Game.GameFileTypes.PKG Then
                    NewCopyWindow.BackupPath = SelectedPS3Game.GameFilePath
                End If

                If SelectedPS3Game.GameCoverSource IsNot Nothing Then
                    NewCopyWindow.GameIcon = SelectedPS3Game.GameCoverSource
                End If

                If NewCopyWindow.ShowDialog() = True Then
                    MsgBox("Game copied with success !", MsgBoxStyle.Information, "Completed")
                End If
            End If

        End If
    End Sub

#End Region

#Region "Library Menu Actions"

    Private Sub LoadFolderMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles LoadFolderMenuItem.Click
        Dim FBD As New Forms.FolderBrowserDialog() With {.Description = "Select your PS3 backup folder"}
        If FBD.ShowDialog() = Forms.DialogResult.OK Then

            PS3GamesListView.Items.Clear()

            FoldersCount = Directory.GetFiles(FBD.SelectedPath, "*.SFO", SearchOption.AllDirectories).Count
            ISOCount = Directory.GetFiles(FBD.SelectedPath, "*.iso", SearchOption.AllDirectories).Count
            PKGCount = Directory.GetFiles(FBD.SelectedPath, "*.pkg", SearchOption.AllDirectories).Count

            'Show the loading progress window
            NewLoadingWindow = New SyncWindow() With {.Title = "Loading PS3 files", .ShowActivated = True}
            NewLoadingWindow.LoadProgressBar.Maximum = FoldersCount + ISOCount + PKGCount
            NewLoadingWindow.LoadStatusTextBlock.Text = "Loading file 1 of " + (FoldersCount + ISOCount + PKGCount).ToString()
            NewLoadingWindow.Show()

            GameLoaderWorker.RunWorkerAsync(New GameLoaderArgs() With {.Type = LoadType.BackupFolder, .FolderPath = FBD.SelectedPath})
        End If
    End Sub

    Private Sub LoadRemoteFolderMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles LoadRemoteFolderMenuItem.Click
        If PS3GamesListView.Items.Count > 0 Then
            Dim MessgBoxResult As MsgBoxResult = MsgBox("Games library already contains games." + vbCrLf + "Do you want to clear before proceeding ?", MsgBoxStyle.YesNoCancel, "Clear previous games ?")

            If MessgBoxResult = MsgBoxResult.Yes Then
                PS3GamesListView.Items.Clear()
            ElseIf MessgBoxResult = MsgBoxResult.No Then
                If Not String.IsNullOrEmpty(ConsoleIP) Then
                    'Show the loading progress window
                    NewLoadingWindow = New SyncWindow() With {.Title = "Loading PS3 files", .ShowActivated = True}
                    NewLoadingWindow.LoadProgressBar.IsIndeterminate = True
                    NewLoadingWindow.LoadStatusTextBlock.Text = "Loading files, please wait ..."
                    NewLoadingWindow.Show()

                    'Load the files
                    GameLoaderWorker.RunWorkerAsync(New GameLoaderArgs() With {.Type = LoadType.FTP, .ConsoleIP = ConsoleIP, .FolderPath = String.Empty})
                Else
                    Dim NewInputDialog As New InputDialog With {
                    .Title = "Enter PS3 IP Address",
                    .NewValueTextBox_Text = "0.0.0.0",
                    .InputDialogTitleTextBlock_Text = "Please enter your PS3 IP address :",
                    .ConfirmButton_Text = "Confirm"
                }

                    If NewInputDialog.ShowDialog() = True Then
                        Dim InputDialogResult As String = NewInputDialog.NewValueTextBox_Text

                        'Show the loading progress window
                        NewLoadingWindow = New SyncWindow() With {.Title = "Loading PS3 files", .ShowActivated = True}
                        NewLoadingWindow.LoadProgressBar.IsIndeterminate = True
                        NewLoadingWindow.LoadStatusTextBlock.Text = "Loading files, please wait ..."
                        NewLoadingWindow.Show()

                        'Load the files
                        GameLoaderWorker.RunWorkerAsync(New GameLoaderArgs() With {.Type = LoadType.FTP, .ConsoleIP = InputDialogResult, .FolderPath = String.Empty})
                    End If
                End If
            End If
        Else
            If Not String.IsNullOrEmpty(ConsoleIP) Then
                'Show the loading progress window
                NewLoadingWindow = New SyncWindow() With {.Title = "Loading PS3 files", .ShowActivated = True}
                NewLoadingWindow.LoadProgressBar.IsIndeterminate = True
                NewLoadingWindow.LoadStatusTextBlock.Text = "Loading files, please wait ..."
                NewLoadingWindow.Show()

                'Load the files
                GameLoaderWorker.RunWorkerAsync(New GameLoaderArgs() With {.Type = LoadType.FTP, .ConsoleIP = ConsoleIP, .FolderPath = String.Empty})
            Else
                Dim NewInputDialog As New InputDialog With {
                .Title = "Enter PS3 IP Address",
                .NewValueTextBox_Text = "0.0.0.0",
                .InputDialogTitleTextBlock_Text = "Please enter your PS3 IP address :",
                .ConfirmButton_Text = "Confirm"
            }

                If NewInputDialog.ShowDialog() = True Then
                    Dim InputDialogResult As String = NewInputDialog.NewValueTextBox_Text

                    'Show the loading progress window
                    NewLoadingWindow = New SyncWindow() With {.Title = "Loading PS3 files", .ShowActivated = True}
                    NewLoadingWindow.LoadProgressBar.IsIndeterminate = True
                    NewLoadingWindow.LoadStatusTextBlock.Text = "Loading files, please wait ..."
                    NewLoadingWindow.Show()

                    'Load the files
                    GameLoaderWorker.RunWorkerAsync(New GameLoaderArgs() With {.Type = LoadType.FTP, .ConsoleIP = InputDialogResult, .FolderPath = String.Empty})
                End If
            End If
        End If
    End Sub

    Private Sub LoadDLFolderMenuItem_Click(sender As Object, e As RoutedEventArgs) Handles LoadDLFolderMenuItem.Click
        If Directory.Exists(My.Computer.FileSystem.CurrentDirectory + "\Downloads") Then
            Process.Start(My.Computer.FileSystem.CurrentDirectory + "\Downloads")
        End If
    End Sub

#End Region

    Private Sub PS3GamesListView_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles PS3GamesListView.SelectionChanged
        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedPS3Game As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)

            GameTitleTextBlock.Text = SelectedPS3Game.GameTitle
            GameIDTextBlock.Text = "Title ID: " & SelectedPS3Game.GameID
            GameContentIDTextBlock.Text = "Content ID: " & SelectedPS3Game.ContentID
            GameRegionTextBlock.Text = "Region: " & SelectedPS3Game.GameRegion
            GameVersionTextBlock.Text = "Game Version: " & SelectedPS3Game.GameVer
            GameAppVersionTextBlock.Text = "Application Version: " & SelectedPS3Game.GameAppVer
            GameCategoryTextBlock.Text = "Category: " & SelectedPS3Game.GameCategory
            GameSizeTextBlock.Text = "Size: " & SelectedPS3Game.GameSize
            GameRequiredFirmwareTextBlock.Text = "Required Firmware: " & SelectedPS3Game.GameRequiredFW

            ResolutionsImage.ToolTip = SelectedPS3Game.GameResolution
            SoundFormatsImage.ToolTip = SelectedPS3Game.GameSoundFormat

            GameBackupTypeTextBlock.Text = "Backup Type: " & SelectedPS3Game.GameFileType.ToString()

            If Not String.IsNullOrEmpty(SelectedPS3Game.GameFilePath) Then
                GameBackupFolderNameTextBlock.Text = "Backup Folder: " & New DirectoryInfo(Path.GetDirectoryName(SelectedPS3Game.GameFilePath)).Name
            Else
                GameBackupFolderNameTextBlock.Text = "Backup Folder: " & New DirectoryInfo(SelectedPS3Game.GameFolderPath).Name
            End If

            If Not String.IsNullOrEmpty(SelectedPS3Game.GameBackgroundPath) Then
                If Dispatcher.CheckAccess() = False Then
                    Dispatcher.BeginInvoke(Sub()
                                               Dim TempBitmapImage = New BitmapImage()
                                               TempBitmapImage.BeginInit()
                                               TempBitmapImage.CacheOption = BitmapCacheOption.OnLoad
                                               TempBitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache
                                               TempBitmapImage.UriSource = New Uri(SelectedPS3Game.GameBackgroundPath, UriKind.RelativeOrAbsolute)
                                               TempBitmapImage.EndInit()
                                               RectangleImageBrush.ImageSource = TempBitmapImage
                                               BlurringShape.BeginAnimation(OpacityProperty, New DoubleAnimation With {.From = 0, .To = 1, .Duration = New Duration(TimeSpan.FromMilliseconds(500))})
                                           End Sub)
                Else
                    Dim TempBitmapImage = New BitmapImage()
                    TempBitmapImage.BeginInit()
                    TempBitmapImage.CacheOption = BitmapCacheOption.OnLoad
                    TempBitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache
                    TempBitmapImage.UriSource = New Uri(SelectedPS3Game.GameBackgroundPath, UriKind.RelativeOrAbsolute)
                    TempBitmapImage.EndInit()
                    RectangleImageBrush.ImageSource = TempBitmapImage
                    BlurringShape.BeginAnimation(OpacityProperty, New DoubleAnimation With {.From = 0, .To = 1, .Duration = New Duration(TimeSpan.FromMilliseconds(500))})
                End If
            ElseIf SelectedPS3Game.GameBackgroundSource IsNot Nothing Then
                If Dispatcher.CheckAccess() = False Then
                    Dispatcher.BeginInvoke(Sub()
                                               RectangleImageBrush.ImageSource = SelectedPS3Game.GameBackgroundSource
                                               BlurringShape.BeginAnimation(OpacityProperty, New DoubleAnimation With {.From = 0, .To = 1, .Duration = New Duration(TimeSpan.FromMilliseconds(500))})
                                           End Sub)
                Else
                    RectangleImageBrush.ImageSource = SelectedPS3Game.GameBackgroundSource
                    BlurringShape.BeginAnimation(OpacityProperty, New DoubleAnimation With {.From = 0, .To = 1, .Duration = New Duration(TimeSpan.FromMilliseconds(500))})
                End If
            Else
                RectangleImageBrush.ImageSource = Nothing
            End If

            If SelectedPS3Game.GameBackgroundSoundFile IsNot Nothing Then
                If IsSoundPlaying Then
                    Utils.StopGameSound()
                    IsSoundPlaying = False
                Else
                    Utils.PlayGameSound(SelectedPS3Game.GameBackgroundSoundFile)
                    IsSoundPlaying = True
                End If
            ElseIf SelectedPS3Game.GameBackgroundSoundBytes IsNot Nothing Then
                If IsSoundPlaying Then
                    Utils.StopSND()
                    IsSoundPlaying = False
                Else
                    Utils.PlaySND(SelectedPS3Game.GameBackgroundSoundBytes)
                    IsSoundPlaying = True
                End If
            Else
                If IsSoundPlaying Then
                    Utils.StopGameSound()
                    IsSoundPlaying = False
                End If
            End If
        End If
    End Sub

    Private Sub PS3GamesListView_ContextMenuOpening(sender As Object, e As ContextMenuEventArgs) Handles PS3GamesListView.ContextMenuOpening
        NewContextMenu.Items.Clear()
        ISOToolsMenuItem.Items.Clear()

        If PS3GamesListView.SelectedItem IsNot Nothing Then
            Dim SelectedPS3Game As PS3Game = CType(PS3GamesListView.SelectedItem, PS3Game)

            NewContextMenu.Items.Add(CopyToMenuItem)

            Select Case SelectedPS3Game.GameFileType
                Case PS3Game.GameFileTypes.Backup
                    NewContextMenu.Items.Add(PlayMenuItem)
                    NewContextMenu.Items.Add(ISOToolsMenuItem)
                    ISOToolsMenuItem.Items.Add(CreateISOMenuItem)
                Case PS3Game.GameFileTypes.PKG
                    NewContextMenu.Items.Add(PKGInfoMenuItem)
                    NewContextMenu.Items.Add(ExtractPKGMenuItem)
                Case PS3Game.GameFileTypes.PS3ISO
                    If SelectedPS3Game.GameRootLocation = PS3Game.GameLocation.WebMANMOD Then
                        NewContextMenu.Items.Add(ISOToolsMenuItem)
                        ISOToolsMenuItem.Items.Add(MountAndPlayISOMenuItem)
                        ISOToolsMenuItem.Items.Add(MountISOMenuItem)
                    Else
                        NewContextMenu.Items.Add(ISOToolsMenuItem)
                        ISOToolsMenuItem.Items.Add(ExtractISOMenuItem)
                        ISOToolsMenuItem.Items.Add(PatchISOMenuItem)
                        ISOToolsMenuItem.Items.Add(SplitISOMenuItem)
                    End If
                Case PS3Game.GameFileTypes.PS2ISO, PS3Game.GameFileTypes.PSXISO, PS3Game.GameFileTypes.PSPISO
                    NewContextMenu.Items.Add(ISOToolsMenuItem)
                    ISOToolsMenuItem.Items.Add(MountAndPlayISOMenuItem)
                    ISOToolsMenuItem.Items.Add(MountISOMenuItem)
            End Select

        End If
    End Sub

    Private Sub PS3GamesListView_ContextMenuClosing(sender As Object, e As ContextMenuEventArgs) Handles PS3GamesListView.ContextMenuClosing
        NewContextMenu.Items.Clear()
    End Sub

    Private Sub PS3GamesListView_PreviewMouseWheel(sender As Object, e As MouseWheelEventArgs) Handles PS3GamesListView.PreviewMouseWheel
        Dim OpenWindowsListViewScrollViewer As ScrollViewer = Utils.FindScrollViewer(PS3GamesListView)
        Dim HorizontalOffset As Double = OpenWindowsListViewScrollViewer.HorizontalOffset
        OpenWindowsListViewScrollViewer.ScrollToHorizontalOffset(HorizontalOffset - (e.Delta / 100))
        e.Handled = True
    End Sub

#Region "Filtering"

    Private Sub FilterByBackupFoldersButton_Click(sender As Object, e As RoutedEventArgs) Handles FilterByBackupFoldersButton.Click
        PS3GamesListView.Items.Clear()

        For Each PS3GameInList As PS3Game In GamesList.Where(Function(lvi) lvi.GameFileType.Equals(PS3Game.GameFileTypes.Backup))
            PS3GamesListView.Items.Add(PS3GameInList)
        Next
    End Sub

    Private Sub FilterByPS3ISOButton_Click(sender As Object, e As RoutedEventArgs) Handles FilterByPS3ISOButton.Click
        PS3GamesListView.Items.Clear()

        For Each PS3GameInList As PS3Game In GamesList.Where(Function(lvi) lvi.GameFileType.Equals(PS3Game.GameFileTypes.PS3ISO))
            PS3GamesListView.Items.Add(PS3GameInList)
        Next
    End Sub

    Private Sub FilterByPS2ISOButton_Click(sender As Object, e As RoutedEventArgs) Handles FilterByPS2ISOButton.Click
        PS3GamesListView.Items.Clear()

        For Each PS3GameInList As PS3Game In GamesList.Where(Function(lvi) lvi.GameFileType.Equals(PS3Game.GameFileTypes.PS2ISO))
            PS3GamesListView.Items.Add(PS3GameInList)
        Next
    End Sub

    Private Sub FilterByPSXISOButton_Click(sender As Object, e As RoutedEventArgs) Handles FilterByPSXISOButton.Click
        PS3GamesListView.Items.Clear()

        For Each PS3GameInList As PS3Game In GamesList.Where(Function(lvi) lvi.GameFileType.Equals(PS3Game.GameFileTypes.PSXISO))
            PS3GamesListView.Items.Add(PS3GameInList)
        Next
    End Sub

    Private Sub FilterByPSPISOButton_Click(sender As Object, e As RoutedEventArgs) Handles FilterByPSPISOButton.Click
        PS3GamesListView.Items.Clear()

        For Each PS3GameInList As PS3Game In GamesList.Where(Function(lvi) lvi.GameFileType.Equals(PS3Game.GameFileTypes.PSPISO))
            PS3GamesListView.Items.Add(PS3GameInList)
        Next
    End Sub

    Private Sub FilterByPKGButton_Click(sender As Object, e As RoutedEventArgs) Handles FilterByPKGButton.Click
        PS3GamesListView.Items.Clear()

        For Each PS3GameInList As PS3Game In GamesList.Where(Function(lvi) lvi.GameFileType.Equals(PS3Game.GameFileTypes.PKG))
            PS3GamesListView.Items.Add(PS3GameInList)
        Next
    End Sub

    Private Sub FilterByLocalGamesButton_Click(sender As Object, e As RoutedEventArgs) Handles FilterByLocalGamesButton.Click
        PS3GamesListView.Items.Clear()

        For Each PS3GameInList As PS3Game In GamesList.Where(Function(lvi) lvi.GameRootLocation.Equals(PS3Game.GameLocation.Local))
            PS3GamesListView.Items.Add(PS3GameInList)
        Next
    End Sub

    Private Sub FilterByRemoteGamesButton_Click(sender As Object, e As RoutedEventArgs) Handles FilterByRemoteGamesButton.Click
        PS3GamesListView.Items.Clear()

        For Each PS3GameInList As PS3Game In GamesList.Where(Function(lvi) lvi.GameRootLocation.Equals(PS3Game.GameLocation.WebMANMOD))
            PS3GamesListView.Items.Add(PS3GameInList)
        Next
    End Sub

    Private Sub ShowAllButton_Click(sender As Object, e As RoutedEventArgs) Handles ShowAllButton.Click
        PS3GamesListView.Items.Clear()

        For Each PS3GameInList As PS3Game In GamesList
            PS3GamesListView.Items.Add(PS3GameInList)
        Next
    End Sub

#End Region

    Private Sub OpenPKGBrowser(sender As Object, e As RoutedEventArgs)
        Dim NewPKGBrowser As New PKGBrowser() With {.Console = "PS3", .ShowActivated = True}
        NewPKGBrowser.Show()
    End Sub

End Class
