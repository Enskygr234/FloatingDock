<div align="center">

# FloatingDock

**Windows 11 ve 10 için havada duran cam görev çubuğu, kendi Başlat menüsü ve üstte müziği gösteren bir ada.**

[![İndir](https://img.shields.io/badge/İndir-FloatingDock.exe-8FC4FF?style=for-the-badge&logo=windows11&logoColor=white)](https://github.com/Enskygr234/FloatingDock/releases/latest/download/FloatingDock.exe)
&nbsp;
![Windows 11 / 10](https://img.shields.io/badge/Windows-11%20%7C%2010-0078D4?style=flat-square&logo=windows11)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet)
![Lisans: MIT](https://img.shields.io/badge/lisans-MIT-8FC4FF?style=flat-square)

<img src="docs/baslat.png" width="620" alt="FloatingDock Başlat menüsü">

</div>

---

## Neler var?

### 🪟 Havada duran dock
<img src="docs/dock.png" width="460" alt="Dock">

- Ekranın altından ayrık, yuvarlak köşeli, akrilik cam görünüm
- Sabitlenmiş ve açık uygulamalar; çalışanların altında gösterge, öndeki pencere için mavi çizgi
- **Okunmamış rozetleri:** WhatsApp, Discord, Outlook gibi uygulamaların ikonunda kırmızı sayı. Uygulama Windows'a kendi rozetini bildiriyorsa o, bildirmiyorsa sen bakmadan gelen bildirim sayısı gösterilir; uygulamaya geçince sıfırlanır
- İkonları **sürükleyerek sırala**, dosya ya da uygulamayı dock'a **bırakarak sabitle**
- Uygulamaya sağ tık: yeni pencere, sabitle / kaldır, pencereleri kapat
- **Windows 11 yaslama**: sağ tık → Yaslama düzenleri, sola / sağa yasla, ekranı kapla
- İsteğe bağlı **Görev görünümü** ve **Widget'lar** düğmeleri
- **Sistem tepsisi:** `⌃` düğmesi arka planda çalışan uygulamaların ikonlarını (Discord, Steam, OneDrive…) gösterir; tıkla → aç, sağ tık → uygulamanın kendi menüsü
- İkonun üstüne gelince pencerelerin **canlı önizlemesi** (tıkla → öne getir, ✕ → kapat)
- Fare üzerinde gezerken ikonlar yumuşakça **büyür**
- **Akıllı gizleme:** bir pencere dock'a değince aşağı kayar, fareyi ekranın en altına götürünce geri gelir. Büyütülmüş pencereler ekranın tamamını kullanır
- **Çoklu monitör:** her ekranın altında ayrı bir dock; her biri o ekrandaki pencereleri gösterir, paneller tıklanan dock'un ekranında açılır, Windows tuşu Başlat'ı farenin olduğu ekranda açar. Ekran takıp çıkarınca dock'lar kendiliğinden yeniden dizilir (Ayarlar → Dock)

### ✨ Kendi Başlat menüsü
Dock'taki logoya ya da klavyedeki **Windows tuşuna** basınca açılır.

- Saate göre selamlama, canlı tarih/saat
- **Arama:** uygulamalar, ayarlar (Wi-Fi, Ekran, Windows Update…), dosyalar ve web — ok tuşları + Enter
- Cam kutularda sabitlenen uygulamalar (sağ tık → sabitle / kaldır), son kullanılan dosyalar
- Harf gruplu **tüm uygulamalar** listesi
- Sabitlenenler arasında **ok tuşlarıyla** gezinme
- Kilitle / Uyku / Oturumu kapat / Yeniden başlat / Kapat
- Win+E, Win+D, Win+R gibi kısayollar aynen çalışır. İstersen dock'a sağ tıklayıp Windows tuşunu eski menüye bırakabilirsin

### 📶 Hızlı ayarlar, takvim ve bildirimler
<p>
<img src="docs/wifi-ses.png" width="330" alt="Wi-Fi ve ses paneli">
&nbsp;
<img src="docs/takvim.png" width="310" alt="Takvim paneli">
</p>

- Hızlı ayar kutucukları: Wi-Fi, Bluetooth, Rahatsız etme, sessiz, gece ışığı, ekran alıntısı, yansıt, erişilebilirlik
- Ses ve **parlaklık** kaydırıcıları, dizüstünde **pil** durumu, çalan müzik için **medya kartı**
- Yakındaki ağlar sinyal gücüyle listelenir: tıkla → şifreyi yaz → bağlan. Kayıtlı ağı unut, bağlantıyı kes, Wi-Fi'ı aç/kapat
- Parlayan ses kaydırıcısı, tek tıkla sessize alma, kullanılan hoparlörün adı
- Saniyeli büyük saat ve Türkçe aylık takvim (fare tekerleğiyle aylar arasında gezin)
- **Hava durumu:** dock'ta anlık sıcaklık; tıklayınca kendi paneli açılır: önümüzdeki 2 saatin yağışı, 24 saatlik ve 7 günlük tahmin, nem, rüzgâr, UV, hava kalitesi, gün doğumu/batımı. Takvim panelinde saatlik özet. **Yağmur yaklaşınca adada uyarı.** Şehir yaz ya da **konumunu kullan**; kayıtlı şehirler arasında tek tıkla geç
- **Zamanlayıcı:** takvim panelinden 1 / 5 / 10 / 25 dk geri sayım ya da kronometre; adada geri sayar, bitince haber verir
- **Bildirim geçmişi**: kaçırdığın bildirimler takvim panelinde birikir, saatin yanında nokta belirir

### ⚙️ Ayarlar ve tema
<img src="docs/ayarlar.png" width="330" alt="Ayarlar paneli">

- **Açık / koyu tema** (ya da Windows'u takip et), buz mavisi ya da **Windows vurgu rengi**
- Windows 11'de panel malzemesi: **Akrilik**, **Mica** ya da **Mica Alt**
- Dock boyutu (küçük / orta / büyük), ikon büyütme ve otomatik gizleme aç/kapat
- Dock'a sağ tık → **FloatingDock ayarları…** ya da hızlı ayarlardaki **Ayarlar** kutucuğu
- Windows'ta "animasyon efektleri" kapalıysa hareketler de kapanır

### 🎵 Ada (ekranın üstü)
<img src="docs/ada-bildirim.png" width="460" alt="Ada - bildirim">

- Spotify, YouTube ve tarayıcıda **çalan şey**: kapak, şarkı/sanatçı, kapağın renginde ekolayzer
- Üstüne gelince büyür: ilerleme çubuğu ve ⏮ ⏯ ⏭ düğmeleri
- Kapağa ya da şarkı adına tıkla → **Şimdi çalıyor** oynatıcısı: büyük kapak, panel kapağın rengini alır, sürükleyerek şarkıda ileri/geri git, karıştır / tekrarla, **yalnızca Spotify'ın sesi** için ayrı kaydırıcı
- Dock'taki Spotify ikonunda çalarken küçük ekolayzer; üstünde **fare tekerleği** yalnızca Spotify'ın sesini açıp kısar, sağ tıkla oynat/duraklat ve sonraki şarkı
- Gelen **bildirimleri** uygulama ikonu, başlık ve metinle gösterir (eski tip tepsi balonları dahil)
- İstersen **Windows'un kendi bildirim balonlarını gizler**: bildirimler yalnız adada görünür
- **Ses ve parlaklık göstergesi:** değiştirince adada kısa bir çubuk
- **Zamanlayıcı:** kalan süre adada; üstüne gelince duraklat, +1 dk, durdur
- Dizüstünde **şarja takınca** dolan pil animasyonu, **%20 ve %10'da** pil uyarısı
- Tam ekran oyun/videoda kendini gizler

## Kurulum

1. **[FloatingDock.exe dosyasını indir](https://github.com/Enskygr234/FloatingDock/releases/latest/download/FloatingDock.exe)** (69 MB, .NET kurmaya gerek yok) — ya da [sitemden](https://enskygr234.github.io/?q=FloatingDock)
2. Çalıştır. İmzasız olduğu için Windows SmartScreen uyarı verebilir: **Ek bilgi → Yine de çalıştır**
3. Açılışta otomatik başlaması için dock'a **sağ tıkla → Windows açılışında başlat**

Gereksinim: **Windows 11** ya da **Windows 10 (1809 ve üzeri)**. Aynı exe iki sistemde de çalışır; Windows 10'da paneller Win10 tarzı keskin köşeli ve ince kenarlı açılır, cam bulanıklığı Windows 10'un kendi akrilik efektiyle (1803+) yapılır.

## Kaldırma / eski görev çubuğuna dönme

Dock'a **sağ tıkla → Dock'u kapat**. Windows görev çubuğu eski ayarlarıyla geri gelir.
Otomatik başlatmayı açtıysan önce aynı menüden kapat, sonra `FloatingDock.exe` dosyasını silebilirsin.

Program beklenmedik şekilde kapanırsa ve görev çubuğu geri gelmezse: `Ctrl+Shift+Esc` → Görev Yöneticisi → **Windows Gezgini**'ni yeniden başlat, ardından *Ayarlar → Kişiselleştirme → Görev çubuğu → Görev çubuğu davranışları → Görev çubuğunu otomatik olarak gizle* seçeneğini kapat.

## Gizlilik

- Varsayılan olarak internete **hiçbir şey göndermez**. (Başlat menüsünde "web'de ara"ya basarsan arama yalnızca o an varsayılan tarayıcında açılır.)
- Tek istisna, kendin açarsan **hava durumu**: ayarlara girdiğin şehir adı bir kez [open-meteo.com](https://open-meteo.com)'a gönderilip koordinata çevrilir, sonra 15 dakikada bir o koordinatın tahmini ve hava kalitesi alınır. **Konumumu kullan** dersen koordinat Windows konum servisinden gelir ve yer adını bulmak için [bigdatacloud.net](https://www.bigdatacloud.com)'e de gönderilir. Başka veri gönderilmez; şehir kutusunu boşaltınca kapanır.
- **Windows bildirim balonlarını gizle** açıksa her uygulamanın Windows ayarlarındaki "Bildirim başlıklarını göster" seçeneği kapatılır (bildirim merkezine yazılmaya devam eder). Eski değerler saklanır; ayar kapanınca ya da dock kapanınca hepsi eski hâline döner.
- Sistem tepsisi için FloatingDock bir tepsi sunucusu çalıştırır ([ManagedShell](https://github.com/cairoshell/ManagedShell), Apache-2.0). Dock kapanınca ikonlar Windows görev çubuğuna geri döner.
- Çalan medya bilgisi Windows'un kendi medya arayüzünden alınır.
- Bildirimler, Windows'un yerel bildirim veritabanından (`%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db`) **sadece okunarak** alınır; hiçbir şey kaydedilmez veya değiştirilmez.
- Wi-Fi şifreleri yalnızca Windows'un kendi kayıtlı ağ profiline yazılır (Windows ayarlarından bağlanmakla aynı); yanlış şifreyle oluşturulan profil hemen silinir.
- Bildirim geçmişi yalnızca bellekte tutulur, diske yazılmaz; program kapanınca silinir.
- Ayarlar `%LOCALAPPDATA%\FloatingDock\` klasöründe (`settings.json`) ve `HKCU\Software\FloatingDock` altında durur; hata olursa kayıt `error.log` dosyasına yazılır.

## Bilinen sınırlar

- İndirme ilerlemesi adada gösterilmiyor: uygulamalar bunu yalnız Windows'un kendi görev çubuğuna bildiriyor.
- Windows'un kendi ses göstergesi de çıkmaya devam eder.
- Sadece ana ekranda çalışır; diğer monitörlerde Windows görev çubuğu kalır.
- Kurumsal (kullanıcı adı + şifre isteyen, 802.1X) Wi-Fi ağlarına panelden bağlanılamaz; panel bu ağlar için Windows ayarlarını açar.
- "Windows bildirim balonlarını gizle" açıkken, daha önce hiç bildirim göndermemiş bir uygulamanın **ilk** bildirimi bir kez Windows balonu olarak da çıkabilir.

## Derleme

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Panelleri dock olmadan denemek için: `FloatingDock.exe --panel baslat|wifi|takvim|ayarlar|tepsi [saniye]` (ekran görüntüsü için örnek verilerle: sonuna `--demo` ekle).

C# / WPF, .NET 10 · Lisans: [MIT](LICENSE) · Yapan: [Winterdeveloper](https://enskygr234.github.io)
