# FloatingDock ❄️

Windows 11 için havada duran, buzlu cam görünümlü görev çubuğu ve ekranın üstünde **çalan müziği ve bildirimleri** gösteren bir "ada".

![Dock](docs/dock.png)

![Ada - bildirim](docs/ada-bildirim.png)

## Özellikler

**Dock**
- Ekranın altından ayrık, yuvarlak köşeli, akrilik cam görünüm
- Sabitlenmiş ve açık uygulamalar; açık olanların altında gösterge, öndeki pencere için mor çizgi
- Tıkla: aç / öne getir / küçült; birden çok pencerede sıradakine geç
- İkonun üstüne gelince pencerelerin **canlı önizlemesi** (tıkla → öne getir, ✕ → kapat)
- Fare üzerinde gezerken ikonlar yumuşakça **büyür**
- **Akıllı gizleme:** bir pencere dock'a değince aşağı kayar, fareyi ekranın en altına götürünce geri gelir. Büyütülmüş pencereler ekranın tamamını kullanır
- Başlat ve arama düğmeleri

**Kendi panelleri (Windows'un eski panelleri yerine)**
- **Wi-Fi ve ses:** yakındaki ağlar sinyal gücüyle listelenir; tıkla → şifreyi yaz → bağlan. Kayıtlı ağı unutma, bağlantıyı kesme, Wi-Fi'ı açıp kapatma
- Parlayan ses kaydırıcısı, tek tıkla sessize alma, hangi hoparlörün kullanıldığı
- **Takvim:** saniyeli büyük saat, Türkçe aylık takvim (fare tekerleğiyle aylar arasında gezin, ay adına tıkla → bugüne dön)

**Ada (ekranın üstü)**
- Spotify, YouTube ve tarayıcıda **çalan şey**: kapak, şarkı/sanatçı, kapağın renginde ekolayzer
- Üstüne gelince büyür: ilerleme çubuğu ve ⏮ ⏯ ⏭ düğmeleri
- Gelen **bildirimleri** uygulama ikonu, başlık ve metinle gösterir
- Tam ekran oyun/videoda kendini gizler

## Kurulum

1. **[FloatingDock.exe dosyasını indir](https://github.com/Enskygr234/winter-browser-releases/releases/download/wd-floatingdock/FloatingDock.exe)** (69 MB) — ya da [sitemden](https://enskygr234.github.io/?q=FloatingDock)
2. Çalıştır. (İmzasız olduğu için Windows SmartScreen uyarı verebilir: **Ek bilgi → Yine de çalıştır**)
3. Açılışta otomatik başlaması için dock'a **sağ tıkla → Windows açılışında başlat**

Gereksinim: Windows 11 (Windows 10'da da çalışır ama cam efekti ve köşeler farklı görünür). .NET kurmaya gerek yok.

## Kaldırma / eski görev çubuğuna dönme

Dock'a **sağ tıkla → Dock'u kapat**. Windows görev çubuğu eski ayarlarıyla geri gelir.
Otomatik başlatmayı açtıysan önce aynı menüden kapat, sonra `FloatingDock.exe` dosyasını silebilirsin.

Program beklenmedik şekilde kapanırsa ve görev çubuğu geri gelmezse: `Ctrl+Shift+Esc` → Görev Yöneticisi → **Windows Gezgini**'ni yeniden başlat, ardından *Ayarlar → Kişiselleştirme → Görev çubuğu → Görev çubuğu davranışları → Görev çubuğunu otomatik olarak gizle* seçeneğini kapat.

## Gizlilik

- İnternete **hiçbir şey göndermez**, hiçbir sunucuya bağlanmaz.
- Wi-Fi şifreleri yalnızca Windows'un kendi kayıtlı ağ profiline yazılır (Windows ayarlarından bağlanmakla aynı); program şifreyi başka yerde saklamaz. Yanlış şifreyle oluşturulan profil hemen silinir.
- Çalan medya bilgisi Windows'un kendi medya arayüzünden alınır.
- Bildirimler, Windows'un yerel bildirim veritabanından (`%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db`) **sadece okunarak** alınır; hiçbir şey kaydedilmez veya değiştirilmez.
- Hata olursa kayıt yalnızca `%LOCALAPPDATA%\FloatingDock\error.log` dosyasına yazılır.

## Bilinen sınırlar

- Sistem tepsisi (arka plan uygulama ikonları) yok.
- Kurumsal (kullanıcı adı + şifre isteyen, 802.1X) Wi-Fi ağlarına panelden bağlanılamaz; panel bu ağlar için Windows ayarlarını açar.
- Sadece ana ekranda çalışır; diğer monitörlerde Windows görev çubuğu kalır.
- Windows'un kendi bildirim balonu da çıkmaya devam eder. İstersen *Ayarlar → Sistem → Bildirimler*'den balonları kapatabilirsin.

## Derleme

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

C# / WPF, .NET 10. Lisans: MIT.
