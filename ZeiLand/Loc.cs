using System.Globalization;
using System.Windows;

namespace ZeiLand;

/// <summary>
/// The app's texts in Turkish and English. XAML uses them as <c>{DynamicResource Key}</c>, so open windows
/// switch language on the spot; code asks <see cref="T"/> or <see cref="F"/> whenever it writes a text.
/// </summary>
internal static class Loc
{
    private static readonly Dictionary<string, (string Tr, string En)> Texts = new()
    {
        // ---- Island ----
        ["OptimizeTitle"] = ("Kaynak tüketenler", "Heavy apps"),
        ["OptimizeHint"] = ("Kapat: uygulama kaydetmek isterse sana sorar.", "Close: the app asks first if there's anything to save."),
        ["SoundTitle"] = ("Ses", "Sound"),
        ["SoundDevices"] = ("Ses girişi ve çıkışı", "Sound input and output"),
        ["OutputTab"] = ("Çıkış", "Output"),
        ["InputTab"] = ("Giriş", "Input"),
        ["NoOutputs"] = ("Ses çıkışı bulunamadı", "No sound output found"),
        ["NoInputs"] = ("Mikrofon bulunamadı", "No microphone found"),
        ["TimerTitle"] = ("Zamanlayıcı", "Timer"),
        ["Stopwatch"] = ("Kronometre", "Stopwatch"),
        ["StartStopwatch"] = ("Kronometreyi başlat", "Start stopwatch"),
        ["Minutes"] = ("{0} dk", "{0} min"),
        ["TimerOfMinutes"] = ("{0} dk zamanlayıcı", "{0} min timer"),
        ["TimesUp"] = ("Süre doldu", "Time's up"),
        ["Ok"] = ("Tamam", "OK"),
        ["MicOff"] = ("Mikrofon kapalı", "Microphone off"),
        ["MicOn"] = ("Mikrofon açık", "Microphone on"),
        ["MicMute"] = ("Mikrofonu kapat", "Mute microphone"),
        ["MicUnmute"] = ("Mikrofonu aç", "Unmute microphone"),
        ["MicrophoneApps"] = ("Mikrofon: {0}", "Microphone: {0}"),
        ["MicrophoneMutedApps"] = ("Mikrofon (kapalı): {0}", "Microphone (muted): {0}"),
        ["CameraApps"] = ("Kamera: {0}", "Camera: {0}"),
        ["RamSummary"] = ("RAM {0} · {1:0.0} GB", "RAM {0} · {1:0.0} GB"),
        ["AppClosed"] = ("{0} kapandı · ~{1} boşaldı", "{0} closed · ~{1} freed"),
        ["Close"] = ("Kapat", "Close"),
        ["CloseTip"] = ("Pencereyi kapatmasını ister", "Asks it to close its window"),
        ["Terminate"] = ("Sonlandır", "End task"),
        ["TerminateTip"] = ("İşlemleri zorla sonlandırır; kaydedilmemiş veri kaybolur", "Forcibly ends its processes; unsaved work is lost"),
        ["AreYouSure"] = ("Emin misin?", "Sure?"),
        ["Closing"] = ("{0} kapatılıyor…", "Closing {0}…"),
        ["Terminating"] = ("{0} sonlandırılıyor…", "Ending {0}…"),
        ["BatteryLow"] = ("Pil zayıf", "Battery low"),
        ["FullyCharged"] = ("Tam şarjlı", "Fully charged"),
        ["Charging"] = ("Şarj oluyor", "Charging"),
        ["OnBattery"] = ("Pil gücüne geçildi", "On battery power"),
        ["DateFormat"] = ("d MMMM dddd", "dddd, MMMM d"),
        ["Updating"] = ("ZeiLand {0} yükleniyor…", "Installing ZeiLand {0}…"),
        ["Updated"] = ("ZeiLand {0} sürümüne güncellendi", "Updated to ZeiLand {0}"),
        ["AddShortcut"] = ("Kısayol ekle", "Add shortcut"),
        ["OptimizeTip"] = ("Optimize: kaynak tüketen uygulamalar", "Optimize: the apps using the most"),
        ["AddMinute"] = ("Bir dakika ekle", "Add a minute"),
        ["PlusMinute"] = ("+1 dk", "+1 min"),
        ["Stop"] = ("Durdur", "Stop"),
        ["Pause"] = ("Duraklat", "Pause"),
        ["Resume"] = ("Devam et", "Resume"),
        ["Paused"] = ("Duraklatıldı", "Paused"),
        ["Connected"] = ("Bağlandı", "Connected"),
        ["Disconnected"] = ("Bağlantı kesildi", "Disconnected"),
        ["ScreenshotTaken"] = ("Ekran görüntüsü", "Screenshot"),
        ["ScreenshotCopied"] = ("Panoya kopyalandı", "Copied to the clipboard"),
        ["Open"] = ("Aç", "Open"),
        ["Save"] = ("Kaydet", "Save"),
        ["ScreenshotSaved"] = ("Ekran Görüntüleri'ne kaydedildi", "Saved to the Screenshots folder"),
        ["ScreenshotSaveFailed"] = ("Ekran görüntüsü kaydedilemedi", "Couldn't save the screenshot"),
        ["InCall"] = ("Görüşmede: {0}", "In a call: {0}"),

        // ---- Menu ----
        ["Settings"] = ("Ayarlar", "Settings"),
        ["Exit"] = ("Çıkış", "Exit"),

        // ---- Settings: tabs, island ----
        ["SettingsTitle"] = ("ZeiLand Ayarları", "ZeiLand Settings"),
        ["TabIsland"] = ("Ada", "Island"),
        ["TabAppearance"] = ("Görünüm", "Appearance"),
        ["TabShortcuts"] = ("Kısayollar", "Shortcuts"),
        ["TabBehaviour"] = ("Davranış", "Behaviour"),
        ["TabAbout"] = ("Hakkında", "About"),
        ["ShowOnIsland"] = ("Adada göster", "Show on the island"),
        ["PlayingMedia"] = ("Çalan medya", "Now playing"),
        ["PlayingMediaTip"] = ("Şarkı adı, kapak ve oynatma kontrolleri", "Song title, cover art and playback controls"),
        ["AnimateBars"] = ("Seviye çubuklarını oynat", "Animate level bars"),
        ["MediaSources"] = ("Kaynaklar: işareti kaldırılan uygulamanın medyası adada gösterilmez",
            "Sources: media from unticked apps isn't shown on the island"),
        ["MediaSourcesEmpty"] = ("Bir uygulama medya çalınca burada görünür.", "Apps show up here once they play something."),
        ["AnimateBarsTip"] = ("Çalan sesin seviyesine göre hareket eder", "They move with the playing audio"),
        ["PrivacyLights"] = ("Mikrofon ve kamera ışıkları", "Microphone and camera lights"),
        ["PrivacyLightsTip"] = ("Bir uygulama mikrofonu (turuncu) veya kamerayı (yeşil) kullanırken; ada kapalıyken de görünür",
            "When an app uses the microphone (orange) or the camera (green); shown even with the island closed"),
        ["VolumeIndicator"] = ("Ses göstergesi", "Volume indicator"),
        ["VolumeIndicatorTip"] = ("Ses seviyesi veya çıkış cihazı değişince kısa süre görünür", "Shows briefly when the volume or the output device changes"),
        ["PowerNotices"] = ("Şarj bildirimleri", "Charging notices"),
        ["PowerNoticesTip"] = ("Şarj takılınca/çıkınca ve pil zayıfken (yalnızca pilli cihazlar)",
            "When the charger is plugged in or out and when the battery is low (battery devices only)"),
        ["ClockDate"] = ("Saat ve tarih", "Clock and date"),
        ["ClockDateTip"] = ("Medya çalmıyorken ada genişleyince", "In the open island when nothing is playing"),
        ["SystemStats"] = ("Sistem bilgileri", "System stats"),
        ["SystemStatsTip"] = ("Ada genişleyince kullanım ve sıcaklıklar, optimize düğmesiyle", "Usage and temperatures in the open island, with an optimize button"),
        ["BluetoothNotices"] = ("Bluetooth kulaklık ve hoparlörler", "Bluetooth headsets and speakers"),
        ["BluetoothNoticesTip"] = ("Bağlanınca ve bağlantı kesilince, bildiriyorsa pil seviyesiyle",
            "When one connects or disconnects, with its battery level if it reports one"),
        ["ScreenshotPreview"] = ("Ekran görüntüsü önizlemesi", "Screenshot preview"),
        ["ScreenshotPreviewTip"] = ("Win+Shift+S ya da Ekran Alıntısı Aracı'ndan sonra; açmak veya kaydetmek için",
            "After Win+Shift+S or the Snipping Tool; to open or save it"),
        ["CallIndicator"] = ("Görüşme göstergesi", "Call indicator"),
        ["CallIndicatorTip"] = ("Discord, Teams, Zoom gibi bir uygulamada görüşürken süreyle birlikte",
            "While you're in a call in Discord, Teams, Zoom and the like, with how long it's been"),
        ["IslandNotifications"] = ("Bildirimler", "Notifications"),
        ["IslandNotificationsTip"] = ("Windows bildirimlerini (mesajlar vb.) adada da gösterir. İçerikleri ekranda görünür; ekran paylaşırken dikkat.",
            "Shows Windows notifications (messages etc.) on the island too. Their content is on screen; mind it while sharing your screen."),
        ["CpuTemperature"] = ("CPU sıcaklığı", "CPU temperature"),
        ["InstallPawnIO"] = ("PawnIO'yu kur", "Install PawnIO"),
        ["RestartAsAdmin"] = ("Yönetici olarak yeniden başlat", "Restart as administrator"),
        ["Off"] = ("Kapalı", "Off"),
        ["CpuTempOffTip"] = ("Sistem bilgilerinde CPU ve CPU °C açıkken gösterilir.", "Shown when CPU and CPU °C are on in the system stats."),
        ["InstallingPawnIO"] = ("PawnIO kuruluyor…", "Installing PawnIO…"),
        ["FinishSteps"] = ("Açılan penceredeki adımları tamamla.", "Finish the steps in the window that opened."),
        ["DriverNeeded"] = ("Sürücü gerekli", "Driver needed"),
        ["DriverNeededTip"] = ("Windows CPU sıcaklığını yalnızca bir sürücü üzerinden verir. PawnIO açık kaynak ve imzalı; bir kez kurulur.",
            "Windows only gives out the CPU temperature through a driver. PawnIO is open source and signed; it's installed once."),
        ["AdminNeeded"] = ("Yönetici modu gerekli", "Administrator mode needed"),
        ["AdminNeededTip"] = ("PawnIO kurulu. Sıcaklığı okumak için ZeiLand'in yönetici olarak çalışması gerekiyor. \"Windows ile başlat\" açıksa her oturumda bunu kendisi yapar.",
            "PawnIO is installed. ZeiLand has to run as administrator to read the temperature. With \"Start with Windows\" on, it does that by itself at every sign-in."),
        ["Working"] = ("Çalışıyor", "Working"),
        ["WorkingTemperature"] = ("Çalışıyor · {0:0}°C", "Working · {0:0}°C"),
        ["WorkingTip"] = ("Ada genişleyince CPU kullanımının yanında görünür.", "Shown next to the CPU usage in the open island."),
        ["Unreadable"] = ("Okunamadı", "Couldn't read"),
        ["UnreadableTip"] = ("Bu işlemcinin sıcaklık sensörüne ulaşılamadı.", "This processor's temperature sensor couldn't be reached."),
        ["SensorStarting"] = ("Sensör açılıyor…", "Starting the sensor…"),

        // ---- Settings: appearance ----
        ["WhenClosed"] = ("Kapalıyken", "When closed"),
        ["WhenClosedTip"] = ("Büyütülmüş bir pencere (ör. tarayıcı sekmeleri) adanın altına uzandığında, müzik çalarken:",
            "When a maximised window (e.g. browser tabs) reaches up under the island while music plays:"),
        ["MiniPlayer"] = ("Mini oynatıcı", "Mini player"),
        ["MiniPlayerTip"] = ("Şarkı ve zamanlayıcı hep görünür kalır", "The song and the timer always stay in view"),
        ["ThinLine"] = ("İnce çizgi", "Thin line"),
        ["ThinLineTip"] = ("Pencereye yer açar; çalan bir şey olduğunu renkli bir çizgi gösterir", "Makes room for the window; a coloured line shows something is playing"),
        ["Position"] = ("Konum", "Position"),
        ["PositionTip"] = ("Taşımak için Ctrl'ye basılı tutup adayı sürükle: üst kenar boyunca, başka bir ekrana da.",
            "To move it, hold Ctrl and drag the island: along the top edge, or onto another screen."),
        ["Centre"] = ("Ortaya al", "Centre"),
        ["IslandSize"] = ("Ada boyutu", "Island size"),
        ["IslandSizeTip"] = ("Yüksek çözünürlüklü ekranlarda büyütmek için", "To make it bigger on high-resolution screens"),
        ["MainScreen"] = ("Ana ekran", "Main screen"),
        ["ScreenNumber"] = ("Ekran {0}", "Screen {0}"),
        ["PlaceCentred"] = ("{0}, ortada", "{0}, centred"),
        ["PlaceLeft"] = ("{0}, sola kaydırılmış", "{0}, moved left"),
        ["PlaceRight"] = ("{0}, sağa kaydırılmış", "{0}, moved right"),
        ["GifInPlayer"] = ("Oynatıcıda GIF", "GIF in the player"),
        ["PlayerButtons"] = ("Oynatıcı ve hızlı düğmeler", "Player and quick buttons"),
        ["SkipButtons"] = ("İleri / geri sarma", "Skip back / ahead"),
        ["SkipButtonsTip"] = ("Sarılabilen oynatıcılarda", "For players that can skip"),
        ["Seconds"] = ("{0} sn", "{0} s"),
        ["QuickButtons"] = ("Hızlı düğmeler", "Quick buttons"),
        ["QuickButtonsTip"] = ("Açık adanın sağında; kapatılanlar gösterilmez", "On the right of the open island; the ones turned off aren't shown"),
        ["TimerButton"] = ("Zamanlayıcı / kronometre", "Timer / stopwatch"),
        ["SoundButton"] = ("Ses giriş / çıkış", "Sound in / out"),
        ["MicButton"] = ("Mikrofon", "Microphone"),
        ["PickGif"] = ("GIF seç", "Choose GIF"),
        ["Remove"] = ("Kaldır", "Remove"),
        ["GifTip"] = ("Açık oynatıcının sol altında, kontrollerin yanında oynar.", "Plays at the bottom left of the open player, next to the controls."),
        ["GifPlaying"] = ("Açık oynatıcının sol altında oynuyor.", "Playing at the bottom left of the open player."),
        ["GifMissing"] = ("GIF bulunamadı; yenisini seç.", "GIF not found; choose a new one."),
        ["GifCopyFailed"] = ("GIF kopyalanamadı.", "Couldn't copy the GIF."),
        ["Preparing"] = ("Hazırlanıyor…", "Preparing…"),
        ["GifFailed"] = ("Bu dosya gösterilemedi: bozuk ya da çok büyük (en fazla 20 MB) bir GIF.", "Couldn't show this file: a damaged or too large GIF (20 MB at most)."),

        // ---- Settings: shortcuts ----
        ["ShortcutsTip"] = ("Açık adada simge olarak dururlar; tıklayınca açılırlar. En fazla 8 tane.", "They sit as icons in the open island and open with a click. Up to 8."),
        ["Add"] = ("Ekle", "Add"),
        ["App"] = ("Uygulama", "App"),
        ["File"] = ("Dosya", "File"),
        ["Folder"] = ("Klasör", "Folder"),
        ["Website"] = ("Web sitesi", "Website"),
        ["SearchApps"] = ("Uygulama ara", "Search apps"),
        ["AddressHint"] = ("Adres, ör. youtube.com", "Address, e.g. youtube.com"),
        ["NameHint"] = ("Ad (isteğe bağlı)", "Name (optional)"),
        ["NoShortcuts"] = ("Henüz kısayol yok. Aşağıdan bir uygulama, dosya, klasör veya web sitesi ekle.", "No shortcuts yet. Add an app, file, folder or website below."),
        ["ShortcutsFull"] = ("Kısayollar dolu (8). Yenisi için birini kaldır.", "Shortcuts are full (8). Remove one to add another."),
        ["MoveUp"] = ("Yukarı taşı", "Move up"),
        ["MoveDown"] = ("Aşağı taşı", "Move down"),
        ["AlreadyAdded"] = ("{0} zaten ekli.", "{0} is already there."),
        ["Added"] = ("{0} eklendi; açık adada görünür.", "{0} added; it's in the open island."),
        ["LoadingApps"] = ("Uygulamalar yükleniyor…", "Loading apps…"),
        ["NoMatchingApps"] = ("Eşleşen uygulama yok.", "No matching apps."),
        ["ClickToAdd"] = ("Eklemek için birine tıkla.", "Click one to add it."),
        ["PickFile"] = ("Kısayol için dosya seç", "Choose a file for the shortcut"),
        ["AllFiles"] = ("Tüm dosyalar", "All files"),
        ["PickFolder"] = ("Kısayol için klasör seç", "Choose a folder for the shortcut"),
        ["NotAnAddress"] = ("Bu bir web adresine benzemiyor (ör. youtube.com).", "That doesn't look like a web address (e.g. youtube.com)."),

        // ---- Settings: behaviour ----
        ["Language"] = ("Dil", "Language"),
        ["OpenDelay"] = ("Açılma gecikmesi", "Open delay"),
        ["OpenDelayTip"] = ("Üzerine gelince ne kadar sonra açılsın", "How soon it opens when you hover"),
        ["Instant"] = ("Anında", "Instant"),
        ["Normal"] = ("Normal", "Normal"),
        ["Slow"] = ("Yavaş", "Slow"),
        ["WheelVolume"] = ("Tekerlekle ses", "Wheel for volume"),
        ["WheelVolumeTip"] = ("Fare adanın üstündeyken tekerlek sesi açıp kısar", "Over the island, the mouse wheel turns the volume up and down"),
        ["WheelStep"] = ("Bir tık", "One notch"),
        ["MicShortcut"] = ("Mikrofon kısayolu", "Microphone shortcut"),
        ["MicShortcutTip"] = ("Mikrofonu her yerden kapatıp açar (oyunda, toplantıda); ada da gösterir",
            "Mutes and unmutes the microphone from anywhere (in a game, in a meeting); the island shows it"),
        ["ShortcutTaken"] = ("Bu kısayolu başka bir uygulama kullanıyor; başka bir tane dene.", "Another app uses this shortcut; try a different one."),
        ["PressKeys"] = ("Tuşlara bas…", "Press keys…"),
        ["RecordingHint"] = ("Esc: vazgeç · Geri tuşu: kısayolu kaldır", "Esc: cancel · Backspace: remove the shortcut"),
        ["None"] = ("Yok", "None"),
        ["TimerSound"] = ("Zamanlayıcı sesi", "Timer sound"),
        ["TimerSoundTip"] = ("Süre dolunca Windows'un alarm sesi bir kez çalar", "Plays the Windows alarm sound once when time is up"),
        ["HideFullscreen"] = ("Tam ekranda gizle", "Hide in full screen"),
        ["HideFullscreenTip"] = ("Oyun veya video tam ekrandayken ada kaybolur. Kapalıyken adayı oyunda görmek için oyunu kenarlıksız pencere moduna al.",
            "The island hides while a game or a video is full screen. When off, set a game to borderless to see the island over it."),
        ["StartWithWindows"] = ("Windows ile başlat", "Start with Windows"),
        ["StartWithWindowsTip"] = ("Oturum açılınca yönetici olarak başlar (CPU sıcaklığı için); UAC sormaz",
            "Starts as administrator at sign-in (for the CPU temperature), without a UAC prompt"),

        // ---- Settings: about ----
        ["AboutTagline"] = ("Windows için hafif bir Dynamic Island · ZEIGARN. Hiçbir veri toplamaz.", "A lightweight Dynamic Island for Windows · ZEIGARN. Collects no data."),
        ["Updates"] = ("Güncellemeler", "Updates"),
        ["AutoUpdate"] = ("Otomatik güncelle", "Update automatically"),
        ["AutoUpdateTip"] = ("Yeni sürüm GitHub'dan indirilir ve uygun bir anda (oyun, zamanlayıcı yokken) kendiliğinden kurulur",
            "New versions are downloaded from GitHub and install themselves at a quiet moment (no game, no timer)"),
        ["CheckNow"] = ("Şimdi denetle", "Check now"),
        ["Checking"] = ("Denetleniyor…", "Checking…"),
        ["LastChecked"] = ("Son denetim {0:HH:mm}", "Last checked {0:HH:mm}"),
        ["Downloading"] = ("ZeiLand {0} indiriliyor…", "Downloading ZeiLand {0}…"),
        ["UpdateReady"] = ("ZeiLand {0} hazır", "ZeiLand {0} is ready"),
        ["WillInstallItself"] = ("Uygun bir anda kendiliğinden kurulacak.", "It will install itself at a quiet moment."),
        ["PressInstall"] = ("Kurmak için Yükle'ye bas.", "Press Install to install it."),
        ["Install"] = ("Yükle", "Install"),
        ["InstallingUpdate"] = ("ZeiLand {0} kuruluyor…", "Installing ZeiLand {0}…"),
        ["ComesBackSoon"] = ("Ada birkaç saniye içinde yeniden açılır.", "The island comes back in a few seconds."),
        ["UpToDate"] = ("Güncel", "Up to date"),
        ["CheckFailed"] = ("Denetlenemedi", "Couldn't check"),
        ["TryAgain"] = ("Yeniden dene", "Try again"),
        ["VersionNumber"] = ("Sürüm {0}", "Version {0}"),
        ["UpdatesFromGitHub"] = ("Güncellemeler GitHub sürümlerinden gelir.", "Updates come from the GitHub releases."),
        ["GitHubUnreachable"] = ("GitHub'a ulaşılamadı", "Couldn't reach GitHub"),
        ["DownloadDamaged"] = ("İndirilen dosya bozuk; sonra yeniden denenecek", "The download was damaged; it'll try again later"),
        ["InstallDenied"] = ("Kurulum için izin verilmedi", "Permission to install was denied"),
        ["More"] = ("Diğer", "More"),
        ["WelcomeGuide"] = ("Hoş geldin rehberi", "Welcome guide"),
        ["License"] = ("Lisans (MIT)", "License (MIT)"),
        ["Releases"] = ("Sürümler", "Releases"),
        ["ResetSettings"] = ("Ayarları sıfırla", "Reset settings"),
        ["ResetConfirm"] = ("Emin misin? Tekrar bas", "Sure? Press again"),
        ["ResetDone"] = ("Ayarlar varsayılana döndü (kısayollar yerinde).", "Settings are back to their defaults (shortcuts kept)."),
        ["SettingsSaved"] = ("Ayarlar otomatik kaydedilir.", "Settings are saved automatically."),

        // ---- Always on display ----
        ["TabAod"] = ("AOD", "AOD"),
        ["AodTitle"] = ("Always on display", "Always on display"),
        ["AodIntro"] = ("Kararmış tam ekranda büyük saat, çalan şarkı ve daha fazlası: bilgisayar boştayken ya da hep (ör. ikinci ekranda).",
            "A dimmed full screen with a big clock, what's playing and more: while the computer is idle, or always (e.g. on a second screen)."),
        ["AodEnable"] = ("AOD'yi aç", "Turn on AOD"),
        ["AodEnableTip"] = ("Sağ tık menüsünden de açılıp kapatılır", "Also from the right-click menu"),
        ["AodWhen"] = ("Ne zaman", "When"),
        ["AodIdle"] = ("Boşta kalınca", "When idle"),
        ["AodAlways"] = ("Hep açık", "Always"),
        ["AodWait"] = ("Bekleme", "After"),
        ["AodScreen"] = ("Ekran", "Screen"),
        ["AodIslandScreen"] = ("Adanın ekranı", "Island's screen"),
        ["AodAllScreens"] = ("Tüm ekranlar", "All screens"),
        ["AodShows"] = ("Gösterilenler", "Shows"),
        ["AodClock"] = ("Saat", "Clock"),
        ["AodDate"] = ("Tarih", "Date"),
        ["AodMedia"] = ("Çalan şarkı", "Now playing"),
        ["AodBattery"] = ("Pil", "Battery"),
        ["AodPrivacy"] = ("Mikrofon/kamera", "Mic/camera"),
        ["AodPreview"] = ("Önizle", "Preview"),
        ["AodNote"] = ("Görünürken ekran kapanmaz. Boşta kalınca tam ekran bir oyunun üstüne de açılır (kumanda da girdi sayılır), tam ekran izlenen bir videonun üstüne açılmaz. Hep açıkken tam ekran uygulamanın olduğu ekranı atlar.",
            "While it's up, the screen stays on. When idle it also covers a full-screen game (a controller counts as input), never a full-screen video being watched. Set to always, it skips a screen with a full-screen app."),
        ["AodHintAlways"] = ("Kapatmak için çift tıkla", "Double-click to turn it off"),
        ["AodHintIdle"] = ("Kapatmak için fareyi oynat ya da bir tuşa bas", "Move the mouse or press a key to close"),

        // ---- Welcome ----
        ["WelcomeTitle"] = ("ZeiLand'e hoş geldin", "Welcome to ZeiLand"),
        ["WelcomeIntro"] = ("Ekranının tepesinde küçük, akıllı bir ada. Çalan müzik, zamanlayıcı, mikrofon, sık kullandığın uygulamalar; hepsi tek bir hareket uzağında. Birkaç ipucuyla başlayalım.",
            "A small, smart island at the top of your screen. What's playing, timers, your microphone, the apps you use most: all one move away. Let's start with a few tips."),
        ["HoverTitle"] = ("Üzerine gel, açılsın", "Hover to open"),
        ["HoverLead"] = ("Fareyi adanın üstüne getir", "Move the pointer onto the island"),
        ["HoverText"] = ("Ada büyür: çalan şarkı ve kontrolleri, saat, pil ve sistem bilgileri. Fare ayrılınca kendiliğinden küçülür.",
            "It grows: the song and its controls, the clock, battery and system stats. It shrinks again when the pointer leaves."),
        ["WheelLead"] = ("Tekerlekle ses, çubukta sarma", "Wheel for volume, drag to seek"),
        ["WheelText"] = ("Ada üzerindeyken fare tekerleği sesi açıp kısar. Şarkının ilerleme çubuğuna tıklayıp sürükleyerek istediğin yere gidersin.",
            "Over the island, the mouse wheel turns the volume up and down. Click or drag the song's progress bar to jump anywhere in it."),
        ["MenuLead"] = ("Sağ tık: menü", "Right-click: menu"),
        ["MenuText"] = ("Adaya ya da görev çubuğundaki simgesine sağ tıkla: ayarlar, Windows ile başlat, çıkış.",
            "Right-click the island or its taskbar icon: settings, start with Windows, exit."),
        ["MoveLead"] = ("Ctrl + sürükle: taşı", "Ctrl + drag: move"),
        ["MoveText"] = ("Ctrl'ye basılı tutup adayı üst kenar boyunca ya da başka bir ekrana sürükle. Ortaya yaklaşınca yerine oturur.",
            "Hold Ctrl and drag the island along the top edge or onto another screen. Near the middle, it snaps into place."),
        ["QuickTitle"] = ("Hızlı düğmeler", "Quick buttons"),
        ["TimerLead"] = ("Zamanlayıcı ve kronometre", "Timer and stopwatch"),
        ["TimerText"] = ("Birkaç dakikalık geri sayım ya da kronometre; ada kapalıyken de süre görünür, bitince haber verir.",
            "A countdown of a few minutes or a stopwatch; the time shows even with the island closed, and it tells you when it's done."),
        ["OutputMicLead"] = ("Ses çıkışı ve mikrofon", "Sound output and microphone"),
        ["OutputMicText"] = ("Kulaklık ile hoparlör arasında tek tıkla geç. Mikrofonu adadan{0} kapatıp aç.",
            "Switch between headphones and speakers with one click. Mute the microphone from the island{0}."),
        ["OutputMicShortcut"] = (" ya da her yerden {0} ile", " or from anywhere with {0}"),
        ["ShortcutsLead"] = ("Kendi kısayolların", "Your own shortcuts"),
        ["ShortcutsText"] = ("Sık kullandığın uygulama, klasör ya da siteleri Ayarlar → Kısayollar'dan ekle; açık adada simge olarak durur, tek tıkla açılır.",
            "Add the apps, folders or sites you use most in Settings → Shortcuts; they sit as icons in the open island and open with one click."),
        ["LightsTitle"] = ("Küçük ışıklar", "Little lights"),
        ["LightsLead"] = ("Turuncu mikrofon, yeşil kamera", "Orange microphone, green camera"),
        ["LightsText"] = ("Bir uygulama mikrofonu ya da kamerayı kullanırken ada kapalıyken bile yanar. Üzerine gelince hangi uygulama olduğunu görürsün.",
            "They light up while an app uses the microphone or the camera, even with the island closed. Hover to see which app."),
        ["MutedLead"] = ("Kırmızı: mikrofonun kapalı", "Red: your microphone is muted"),
        ["MutedText"] = ("Görüşmedesin ama mikrofon kapalıysa turuncu nokta yerine kırmızı, üstü çizili bir mikrofon görünür.",
            "In a call with the microphone muted, a red crossed-out microphone shows instead of the orange dot."),
        ["AwayLead"] = ("Yolunda durmaz", "Stays out of the way"),
        ["AwayText"] = ("Tam ekran oyun ve videolarda gizlenir. İstersen (Ayarlar → Görünüm) büyütülmüş bir pencere altına uzanınca ince bir çizgiye döner.",
            "It hides during full-screen games and videos. If you like (Settings → Appearance), it shrinks to a thin line when a maximised window reaches under it."),
        ["ReadyTitle"] = ("Hazırsın", "You're all set"),
        ["ReadyText"] = ("ZeiLand hiçbir veri toplamaz; yalnızca yeni sürüm var mı diye GitHub'a bakar. Bunları sonra Ayarlar'dan da değiştirebilirsin.",
            "ZeiLand collects no data; it only asks GitHub whether there's a new version. You can change these later in Settings."),
        ["WelcomeStartTip"] = ("Oturum açınca ada kendiliğinden gelsin", "The island appears by itself when you sign in"),
        ["WelcomeUpdateTip"] = ("Yeni sürümler uygun bir anda kendiliğinden kurulsun", "New versions install themselves at a quiet moment"),
        ["Back"] = ("Geri", "Back"),
        ["SkipBack"] = ("{0} sn geri", "Back {0} s"),
        ["SkipAhead"] = ("{0} sn ileri", "Ahead {0} s"),
        ["Next"] = ("İleri", "Next"),
        ["Start"] = ("Başla", "Let's go"),
    };

    private static ResourceDictionary? _resources;

    /// <summary>The language changed; code-written texts should be written again.</summary>
    public static event Action? Changed;

    public static bool IsEnglish { get; private set; }

    /// <summary>For dates and numbers in the chosen language.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"tr" or "en"; anything else follows Windows' display language: Turkish if it's Turkish, English otherwise.</summary>
    public static string Resolve(string? language) => language is "tr" or "en" ? language
        : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";

    /// <summary>Switches every text, including those in open windows.</summary>
    public static void Apply(string? language)
    {
        bool english = Resolve(language) == "en";
        if (_resources is not null && english == IsEnglish) return;
        IsEnglish = english;
        Culture = CultureInfo.GetCultureInfo(english ? "en-US" : "tr-TR");

        // A new dictionary in place of the old one: every {DynamicResource} looks itself up again.
        var resources = new ResourceDictionary();
        foreach (var (key, text) in Texts) resources[key] = english ? text.En : text.Tr;
        if (Application.Current is { } app)
        {
            if (_resources is not null) app.Resources.MergedDictionaries.Remove(_resources);
            app.Resources.MergedDictionaries.Add(resources);
        }
        _resources = resources;
        Changed?.Invoke();
    }

    public static string T(string key) => Texts.TryGetValue(key, out var text) ? IsEnglish ? text.En : text.Tr : key;

    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>"%42" in Turkish, "42%" in English.</summary>
    public static string Percent(double fraction)
    {
        double value = Math.Round(fraction * 100);
        return IsEnglish ? $"{value}%" : $"%{value}";
    }
}
