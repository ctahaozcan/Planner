# Yaver — Günlük asistan

Windows için yerel, çevrimdışı günlük planlayıcı ve kişisel asistan. Görevler, ajanda, anımsatıcılar, alışkanlıklar ve şifreli kişi defteri tek uygulamada toplanır.

## Özellikler

- **Bugün**: saatlik şerit (~07:00–22:00, ayarlanabilir), çalışma bandı vurgusu, saatsiz liste, günün notu
- **Bugünün öncelikleri**: günde en fazla 3 görev sabitlenir
- Görev durumları: Başlamadı, Devam Ediyor, Duraklatıldı, Tamamlandı
- **Yineleme**: her gün, haftalık (seçilen günler), aylık (ayın günü), isteğe bağlı bitiş tarihi. Bir oluşumu tamamlamak seriyi silmez; «bu oluşumu atla» veya «yalnızca bu gün» düzenleme
- **Hızlı ekle** ve doğal dil: `yarın 14:00 dişçi`, `cuma fatura`, `her gün 08:00 ilaç` (önizleme çipi). `Ctrl+N` ayrıntılı form, `Ctrl+K` hızlı ekle
- **Genel kısayol** (varsayılan `Ctrl+Alt+N`): tepsideyken bile hızlı ekleme
- **Sabah özeti** (varsayılan 08:00) ve **akşam kapanışı** (21:00)
- **Erteleme**: 10 dk, 1 saat, bu akşam (18:00), yarına — toast düğmeleri ve uygulama içi
- **Sessiz saatler**: toast kuyruğa alınır, aralık bitince iletilir
- **Hafta** görünümü (Pzt–Paz) ve 14 günlük ajanda
- **Alışkanlıklar**: günlük / hafta içi, seri, isteğe bağlı anımsatıcı
- **Odak (Pomodoro)**: varsayılan 25/5, tepside devam eder, bitince toast
- **Görev ekleri**: dosya uygulama verisine kopyalanır (20 MB/dosya)
- **Arama** (`Ctrl+F`): görev, not, kategori, günlük not, alışkanlık; kasa açıkken kişiler
- **Kişiler kasası**: PBKDF2 + AES-256-GCM; doğum günü / yıldönümü kasa açılınca bellek içinde; «Takip et» normal görev oluşturur
- Tepsi + düşük kaynaklı zamanlayıcı (sonraki ana kadar `Task.Delay`, yoklama döngüsü yok)
- Windows ile başlat

## Yedekleme

Ayarlar → Yedekleme:

| İşlem | Açıklama |
| --- | --- |
| Kasa yedeği | Şifreli kişi kayıtları + kasa meta. Şifre doğrulanır. |
| Veritabanı yedeği | Tüm SQLite dosyası AES-GCM ile şifrelenir (`.plnbak`). |
| JSON dışa aktarma | Görev / ajanda / alışkanlık / günlük not. **Kişi PII yok.** |

**Şifreyi unutursanız kişiler ve şifreli yedekler kurtarılamaz.** Arka kapı yoktur. Ayarlar’dan kasa sıfırlanabilir; bu tüm kişileri siler.

Geri yüklenen veritabanından sonra uygulamayı kapatıp yeniden açın.

## Mimari

| Proje | Yol | Rol |
| --- | --- | --- |
| `Planner.sln` | çözüm kökü | |
| `Planner.Core` | `Planner.Core/` | modeller, SQLite (sürümlü göç), şifreleme, zamanlayıcı |
| `Planner.App` | `Planner.App/` | WPF, tepsi, toast, genel kısayol (`Yaver.exe`) |

Veriler `%LOCALAPPDATA%\Yaver\` altındadır:

- `data\planner.db` — SQLite
- `attachments\` — görev ekleri (kasa dışı, şifrelenmez)
- `backups\` — isteğe bağlı kopyalar

Hedef çerçeve: **.NET 10** (Windows 10/11), WPF.

## Anımsatıcılar ve zamanlayıcı

1. Kapatma uygulamayı sonlandırmaz; tepsi simgesi kalır.
2. `ReminderScheduler` bir sonraki olayı hesaplar: görev/erteleme, alışkanlık, sabah özeti, akşam kapanışı, sessiz saat sonu, odak bitişi. **O ana kadar bekler** (`Task.Delay`). Saniyede tarama yoktur.
3. Saat / DST için bekleme en fazla 6 saatte bir yeniden hesaplanır.
4. Veri değişince zamanlayıcı yeniden kurulur.
5. Sessiz saatlerde toast gösterilmez; kuyruk boşaltılır.
6. Tam çıkış: tepsi → **Çıkış**. Genel kısayol çıkışta bırakılır.

## Kişiler şifrelemesi

- İlk girişte en az 8 karakterlik kasa şifresi.
- PBKDF2-SHA256 (210.000 tur) → 256-bit anahtar; diskte tuz + SHA-256 doğrulayıcı.
- Her kişi AES-256-GCM ile şifrelenir.
- Doğum günü / yıldönümü anımsatıcıları kasa açılmadan üretilmez (düz metin kasa dışında tutulmaz). «Takip et» ile oluşturulan görevler normal kayıttır (`Takip: [ad]`).
- **Şifre unutulursa kişiler kurtarılamaz.**

## Kurulum (önerilen)

Gereksinim: Windows 10 1809+ / Windows 11 (64-bit). **.NET kurulumu gerekmez**; paket self-contained’tır.

1. `dist\Yaver-Setup.exe` dosyasına çift tıklayın.
2. Sihirbazı tamamlayın. Uygulama şuraya kurulur: `%LOCALAPPDATA%\Programs\Yaver`
3. Başlat menüsü kısayolu her zaman oluşturulur; masaüstü kısayolu varsayılan olarak işaretlidir.

Kurulum yönetici hakkı istemez. İlk açılışta Windows SmartScreen imzasız dosya uyarısı gösterebilir: **Ek bilgi** → **Yine de çalıştır**.

### Kaldırma

- Windows **Ayarlar → Uygulamalar → Yüklü uygulamalar** içinde **Yaver** → Kaldır
- veya Başlat menüsü → Yaver → **Yaver'ı Kaldır**

Kaldırma uygulama dosyalarını ve kısayolları siler; görev / kişi **verileri durur**.

Eski **Planlayıcı** kurulumu hâlâ listede görünürse onu da kaldırabilirsiniz. Bu, Yaver verisini silmez.

### Verilerin yeri

Uygulama nereden çalışırsa çalışsın veriler şuradadır (depo klasörü değil):

`%LOCALAPPDATA%\Yaver\`

| Yol | İçerik |
| --- | --- |
| `data\planner.db` | SQLite veritabanı |
| `attachments\` | Görev ekleri |
| `backups\` | İsteğe bağlı yedekler |

Verileri de silmek için bu klasörü elle silin.

### Planlayıcı’dan geçiş

Eski uygulama verileri `%LOCALAPPDATA%\Planlayici\` altındaydı. Yaver ilk açılışta kendi klasörü boş veya yoksa bu klasördeki `planner.db`, ekler (`attachments`) ve yedekleri (`backups`) **kopyalar**. Eski klasör otomatik silinmez; isterseniz yedek olarak bırakın, işiniz bitince elle silebilirsiniz.

Windows oturum açılışındaki eski `Planlayici.exe` kaydı kaldırılır; açılışta `Yaver.exe` çalışır.

### Taşınabilir klasör ile kurulum farkı

| | `Yaver-Setup.exe` | `dist\Yaver\` |
| --- | --- | --- |
| Kullanım | Çift tıkla, sihirbaz | Klasörü kopyala, `Yaver.exe` çalıştır |
| Kısayollar | Başlat + masaüstü | Yok (elle oluşturulur) |
| Kaldırma | Windows Uygulamalar listesi | Klasörü sil |
| Veri | `%LOCALAPPDATA%\Yaver` | Aynı (paylaşılır) |
| .NET | Gerekmez | Gerekmez |

Taşınabilir klasör USB / yedek içindir; günlük kullanım için Setup.exe önerilir. İkisini birden çalıştırmayın: aynı veriyi kullanırlar.

Kurulum paketini yeniden üretmek (geliştirici):

```powershell
.\scripts\build-setup.ps1
```

Bu komut self-contained `win-x64` yayını alır ve Inno Setup ile `dist\Yaver-Setup.exe` oluşturur.

## Derleme (kaynak kod)

Gereksinim: [.NET 10 SDK](https://dotnet.microsoft.com/download) ve Windows 10 1809+ / Windows 11.

```powershell
cd "C:\Users\tahat\OneDrive\Masaüstü\Planner"
dotnet build Planner.sln -c Release
dotnet run --project Planner.App\Planner.App.csproj
```

Yalnızca yayın klasörü:

```powershell
.\scripts\publish.ps1
```

Eski PowerShell kopyalama kurulumu (`scripts\install.ps1` / `uninstall.ps1`) hâlâ durur; son kullanıcı için Setup.exe yeterlidir.

## Klavye

| Kısayol | İşlev |
| --- | --- |
| `Ctrl+N` | Ayrıntılı yeni kayıt |
| `Ctrl+K` | Hızlı ekle kutusuna odak |
| `Ctrl+F` | Arama |
| `Ctrl+Alt+N` | Genel hızlı ekle (tepsi dahil; Ayarlar’dan değiştirilir) |
| `Enter` | Hızlı ekle |

## Sınırlamalar

- Çevrimdışı / tek kullanıcı; bulut senkronizasyonu, e-posta istemcisi, hava durumu veya yapay zekâ sohbeti yok.
- Saatlik şeritte sürükle-bırak yok; saate tıklayarak yeni kayıt veya düzenle ile saat verilir.
- Yinelemede «yalnızca bu gün» oluşumu seriden ayırır; karmaşık istisna takvimleri (RRULE) yoktur.
- Odak UI’si görünürken 1 sn güncellenir; gizliyken süre `DateTime` bitişine göre doğrudur.
- Görev ekleri şifrelenmez.
- Kasa şifresi kurtarılamaz.
