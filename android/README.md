# FloatingDock Android

Masaüstündeki FloatingDock'un telefon sürümü: ana ekranı değiştiren bir **launcher**.

- Büyük saat, tarih ve selamlama
- Havada duran **cam dock**: Başlat düğmesi + sabitlediğin uygulamalar
- **Uygulama menüsü**: Başlat düğmesine dokun ya da ana ekranda yukarı kaydır. Arama, harf gruplu tüm uygulamalar, web'de arama
- Uygulamaya **basılı tut**: dock'a sabitle / kaldır, sola / sağa taşı, uygulama bilgisi, kaldır
- **Ada**: ekranın üstünde çalan müzik (kapak, şarkı, ⏮ ⏯ ⏭) ve gelen bildirimler, her uygulamanın üzerinde
- Açık / koyu tema, buz mavisi ya da Material You rengi, **Türkçe / English**
- Ana ekranda boş bir yere basılı tut: **ayarlar**

## Kurulum

1. Telefondan **[FloatingDock.apk](https://github.com/Enskygr234/FloatingDock/releases/download/android-son/FloatingDock.apk)** dosyasını indir ve aç. Telefon "bilinmeyen kaynaklar" için izin isterse ver.
2. Uygulamayı aç; ayarlar ekranındaki **Kurulum** adımlarını yap:
   - **Ana ekran uygulaması yap** → FloatingDock'u seç
   - **Bildirim erişimi ver** → ada müziği ve bildirimleri görebilsin
   - **Diğer uygulamaların üzerinde göster** → ada her yerde görünsün
3. Eski ana ekrana dönmek için: *Ayarlar → Uygulamalar → Varsayılan uygulamalar → Ana ekran uygulaması*.

Android 8.0 ve üzeri gerekir. iPhone'da çalışmaz (Apple ana ekranı değiştirmeye izin vermiyor).

## Bilinen sınırlar

- Android'in üst çubuğu ve aşağı çekilen hızlı ayarlar paneli değiştirilemez.
- Dock yalnız ana ekranda görünür; ada her uygulamanın üstünde görünür (durum çubuğunun hemen altında).
- Henüz ana ekrana widget eklenemez.

## İmza anahtarı (güncellemeler için)

APK'yı GitHub Actions derler. Repoda imza anahtarı **yoktur**. Anahtar eklenmediyse her derleme geçici bir anahtarla imzalanır ve yeni APK eskisinin üstüne kurulamaz (önce eskisini kaldırmak gerekir).

Kalıcı anahtar için bir kez, bilgisayarda:

```
keytool -genkeypair -keystore fd.jks -alias floatingdock -keyalg RSA -keysize 2048 -validity 10000
base64 -w0 fd.jks        (Windows: certutil -encode fd.jks fd.txt)
```

Sonra GitHub'da **Settings → Secrets and variables → Actions** altına iki gizli değer ekle: `FD_KEYSTORE_B64` (base64 çıktısı) ve `FD_KEYSTORE_PASSWORD` (anahtarın şifresi). `fd.jks` dosyasını sakla, repoya koyma.

## Derleme

```
cd android
gradle assembleRelease
```

Kotlin + Jetpack Compose · minSdk 26 · targetSdk 35
