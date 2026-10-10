using System.Globalization;
using System.Text;

namespace Hastane.DevSeed;

public sealed record BranchSeed(string Name, string Description, string Icon);

public sealed record EducationSeed(string School, string Department, int StartYear, int EndYear);

public sealed record ExperienceSeed(string Place, string Position, int StartYear, int? EndYear);

public sealed record AwardSeed(string Title, int Year, string Description);

public sealed record LocationSeed(string ClinicName, string City, string District, string Address, string Floor);

public sealed record DoctorSeed(
    string FirstName,
    string LastName,
    string Title,
    string Gender,
    string Branch,
    string Degrees,
    string Biography,
    string[] Services,
    EducationSeed[] Educations,
    ExperienceSeed[] Experiences,
    AwardSeed[] Awards,
    LocationSeed[] Locations)
{
    public string FullName => $"{FirstName} {LastName}";

    // dr.<ad>.<soyad>@hastane.local, ASCII (ç→c, ğ→g, ı→i, ö→o, ş→s, ü→u).
    public string Email => $"dr.{SeedData.Ascii(FirstName)}.{SeedData.Ascii(LastName)}@hastane.local";
}

public sealed record PatientSeed(string FirstName, string LastName, string Email, string Complaint1, string Complaint2, string Complaint3, string Review, int Rating);

public sealed record MedicineSeed(string Name, string Dosage, string Frequency, int Days, string Instructions);

public static class SeedData
{
    public static readonly BranchSeed[] Branches =
    [
        new("Kardiyoloji", "Kalp ve damar hastalıklarının tanı, tedavi ve takibi.", "heart-pulse"),
        new("Nöroloji", "Beyin, omurilik ve sinir sistemi hastalıkları.", "brain"),
        new("Ortopedi", "Kemik, eklem, kas ve bağ dokusu sorunları ile travma tedavisi.", "bone"),
        new("Göz Hastalıkları", "Görme bozuklukları, katarakt, glokom ve göz sağlığı muayenesi.", "eye"),
        new("Diş Hekimliği", "Ağız ve diş sağlığı, dolgu, kanal tedavisi, implant ve estetik uygulamalar.", "tooth"),
        new("Dahiliye", "Erişkinlerde iç organ hastalıklarının genel değerlendirmesi ve takibi.", "stethoscope"),
        new("Çocuk Sağlığı", "Yenidoğandan ergenliğe çocukların gelişim, aşı ve hastalık takibi.", "baby"),
        new("Dermatoloji", "Cilt, saç ve tırnak hastalıkları ile cilt sağlığı danışmanlığı.", "droplet"),
    ];

    // 12 kurgusal doktor. Dizin sırası avatar numarasını (avatar-01 .. avatar-12) ve ücreti belirler.
    public static readonly DoctorSeed[] Doctors =
    [
        Doc("Ahmet", "Yılmaz", "Prof. Dr.", "Male", "Kardiyoloji", "Tıp Doktoru, Kardiyoloji Uzmanı, Girişimsel Kardiyoloji Yan Dal",
            "Kalp yetmezliği ve koroner arter hastalıkları üzerine yirmi yılı aşkın deneyime sahiptir. Hasta odaklı, anlaşılır bir iletişim anlayışıyla çalışır. Ulusal kongrelerde düzenli sunum yapmaktadır.",
            ["Kalp Muayenesi", "Efor Testi", "Ekokardiyografi", "Ritim Holter"],
            Edu("İstanbul Üniversitesi Cerrahpaşa Tıp Fakültesi", "Tıp", 1996, 2002), Edu("Ankara Üniversitesi Tıp Fakültesi", "Kardiyoloji Uzmanlığı", 2002, 2007),
            Exp("Hastane Kardiyoloji Kliniği", "Klinik Şefi", 2015, null), Exp("Şehir Eğitim ve Araştırma Hastanesi", "Uzman Doktor", 2008, 2015),
            [Award("Yılın Kardiyoloji Hekimi", 2019, "Ulusal Kardiyoloji Derneği"), Award("En İyi Bildiri Ödülü", 2014, "Kardiyoloji Kongresi")],
            Loc("Hastane Ana Poliklinik", "İstanbul", "Kadıköy", "Caferağa Mah. Moda Cad. No:12", "3. Kat")),
        Doc("Elif", "Demir", "Doç. Dr.", "Female", "Kardiyoloji", "Tıp Doktoru, Kardiyoloji Doçenti",
            "Kadınlarda kalp hastalıkları ve hipertansiyon konularında çalışmaktadır. Önleyici kardiyoloji ve yaşam tarzı danışmanlığına önem verir.",
            ["Hipertansiyon Takibi", "EKG", "Kolesterol Yönetimi"],
            Edu("Hacettepe Üniversitesi Tıp Fakültesi", "Tıp", 2000, 2006), Edu("Hacettepe Üniversitesi", "Kardiyoloji Uzmanlığı", 2006, 2011),
            Exp("Üniversite Hastanesi", "Öğretim Üyesi", 2016, null), Exp("Devlet Hastanesi", "Uzman Doktor", 2011, 2016),
            [Award("Genç Araştırmacı Ödülü", 2017, "Türk Kardiyoloji Derneği")],
            Loc("Çankaya Sağlık Merkezi", "Ankara", "Çankaya", "Kızılay Mah. Atatürk Bulvarı No:45", "2. Kat"), Loc("Hastane Ankara Şubesi", "Ankara", "Çankaya", "Çayyolu Mah. Ceyhun Atıf Kansu Cad. No:90", "1. Kat")),
        Doc("Mehmet", "Kaya", "Uzm. Dr.", "Male", "Nöroloji", "Tıp Doktoru, Nöroloji Uzmanı",
            "Baş ağrısı, epilepsi ve inme sonrası rehabilitasyon alanlarında deneyimlidir. Hastalarına ayrıntılı muayene ve açık bilgilendirme sunar.",
            ["Nörolojik Muayene", "EEG", "Migren Tedavisi", "EMG"],
            Edu("Dokuz Eylül Üniversitesi Tıp Fakültesi", "Tıp", 2003, 2009), Edu("Ege Üniversitesi Tıp Fakültesi", "Nöroloji Uzmanlığı", 2009, 2014),
            Exp("Bölge Hastanesi", "Uzman Doktor", 2014, 2020), Exp("Hastane Nöroloji Kliniği", "Uzman Doktor", 2020, null),
            [],
            Loc("Alsancak Tıp Merkezi", "İzmir", "Konak", "Alsancak Mah. Kıbrıs Şehitleri Cad. No:78", "4. Kat")),
        Doc("Zeynep", "Aydın", "Uzm. Dr.", "Female", "Nöroloji", "Tıp Doktoru, Nöroloji Uzmanı, Uyku Tıbbı Sertifikası",
            "Uyku bozuklukları, Parkinson ve demans hastalıklarının takibinde çalışır. Hasta yakınlarını da sürece dahil eden bir yaklaşım benimser.",
            ["Uyku Bozuklukları", "Parkinson Takibi", "Hafıza Değerlendirmesi"],
            Edu("Marmara Üniversitesi Tıp Fakültesi", "Tıp", 2004, 2010), Edu("İstanbul Üniversitesi İstanbul Tıp Fakültesi", "Nöroloji Uzmanlığı", 2010, 2015),
            Exp("Özel Tıp Merkezi", "Uzman Doktor", 2015, 2021), Exp("Hastane Nöroloji Kliniği", "Uzman Doktor", 2021, null),
            [Award("Hasta Memnuniyeti Ödülü", 2022, "Hastane Yönetimi")],
            Loc("Hastane Ana Poliklinik", "İstanbul", "Şişli", "Mecidiyeköy Mah. Büyükdere Cad. No:101", "5. Kat")),
        Doc("Mustafa", "Çelik", "Doç. Dr.", "Male", "Ortopedi", "Tıp Doktoru, Ortopedi ve Travmatoloji Doçenti",
            "Spor yaralanmaları, diz ve omuz artroskopisi üzerine uzmanlaşmıştır. Cerrahi dışı seçenekleri öncelikli değerlendirir.",
            ["Artroskopi", "Spor Yaralanmaları", "Diz Protezi", "Omuz Cerrahisi"],
            Edu("Gazi Üniversitesi Tıp Fakültesi", "Tıp", 1999, 2005), Edu("Gazi Üniversitesi", "Ortopedi ve Travmatoloji Uzmanlığı", 2005, 2010),
            Exp("Üniversite Hastanesi", "Öğretim Üyesi", 2017, null), Exp("Spor Hastanesi", "Uzman Doktor", 2010, 2017),
            [Award("Bilimsel Araştırma Teşvik Ödülü", 2018, "Türk Ortopedi ve Travmatoloji Derneği")],
            Loc("Hastane Ankara Şubesi", "Ankara", "Yenimahalle", "Batıkent Mah. 1802. Cad. No:15", "2. Kat")),
        Doc("Ayşe", "Şahin", "Uzm. Dr.", "Female", "Göz Hastalıkları", "Tıp Doktoru, Göz Hastalıkları Uzmanı",
            "Katarakt, glokom ve göz kuru sendromu tedavisinde deneyimlidir. Çocuk hastalarda şaşılık ve tembel göz takibi de yapar.",
            ["Katarakt Cerrahisi", "Glokom Takibi", "Göz Dibi Muayenesi", "Lazer Tedavisi"],
            Edu("Ege Üniversitesi Tıp Fakültesi", "Tıp", 2002, 2008), Edu("Dokuz Eylül Üniversitesi", "Göz Hastalıkları Uzmanlığı", 2008, 2013),
            Exp("Göz Hastalıkları Hastanesi", "Uzman Doktor", 2013, 2019), Exp("Hastane Göz Kliniği", "Uzman Doktor", 2019, null),
            [],
            Loc("Karşıyaka Poliklinik", "İzmir", "Karşıyaka", "Bostanlı Mah. Cemal Gürsel Cad. No:33", "1. Kat")),
        Doc("Burak", "Öztürk", "Dt.", "Male", "Diş Hekimliği", "Diş Hekimi, İmplantoloji Sertifikalı",
            "Dental implant ve protez uygulamalarında çalışır. Ağrısız ve konforlu tedavi için modern teknikleri tercih eder.",
            ["İmplant", "Diş Beyazlatma", "Kanal Tedavisi", "Porselen Kaplama"],
            Edu("İstanbul Üniversitesi Diş Hekimliği Fakültesi", "Diş Hekimliği", 2006, 2011), Edu("Marmara Üniversitesi", "İmplantoloji Sertifika Programı", 2013, 2014),
            Exp("Ağız ve Diş Sağlığı Merkezi", "Diş Hekimi", 2011, 2018), Exp("Hastane Diş Kliniği", "Diş Hekimi", 2018, null),
            [Award("Yılın Genç Diş Hekimi", 2020, "İstanbul Diş Hekimleri Odası")],
            Loc("Hastane Ana Poliklinik", "İstanbul", "Beşiktaş", "Levent Mah. Büyükdere Cad. No:200", "Zemin Kat")),
        Doc("Selin", "Arslan", "Dt.", "Female", "Diş Hekimliği", "Diş Hekimi, Pedodonti Eğitimi",
            "Çocuk ve ergen diş sağlığı konusunda çalışır. Korkulu hastalar için sakin ve anlayışlı bir ortam sağlar.",
            ["Çocuk Diş Tedavisi", "Fissür Örtücü", "Ortodonti Danışmanlığı"],
            Edu("Ankara Üniversitesi Diş Hekimliği Fakültesi", "Diş Hekimliği", 2007, 2012), Edu("Hacettepe Üniversitesi", "Pedodonti Kursu", 2014, 2015),
            Exp("Çocuk Diş Sağlığı Merkezi", "Diş Hekimi", 2012, 2019), Exp("Hastane Diş Kliniği", "Diş Hekimi", 2019, null),
            [],
            Loc("Çankaya Sağlık Merkezi", "Ankara", "Çankaya", "Kavaklıdere Mah. Tunalı Hilmi Cad. No:60", "3. Kat")),
        Doc("Hakan", "Doğan", "Prof. Dr.", "Male", "Dahiliye", "Tıp Doktoru, İç Hastalıkları Uzmanı, Endokrinoloji Yan Dal",
            "Diyabet, tiroid hastalıkları ve metabolik sendrom konularında kapsamlı değerlendirme yapar. Kronik hastalık takibinde düzenli kontrolü esas alır.",
            ["Diyabet Takibi", "Tiroid Hastalıkları", "Check-up", "Obezite Danışmanlığı"],
            Edu("İstanbul Üniversitesi İstanbul Tıp Fakültesi", "Tıp", 1992, 1998), Edu("İstanbul Üniversitesi", "İç Hastalıkları Uzmanlığı", 1998, 2003),
            Exp("Üniversite Hastanesi", "Öğretim Üyesi", 2012, null), Exp("Eğitim ve Araştırma Hastanesi", "Uzman Doktor", 2003, 2012),
            [Award("Hizmet Ödülü", 2016, "Türk İç Hastalıkları Uzmanlık Derneği"), Award("En İyi Poster Ödülü", 2011, "Endokrinoloji Kongresi")],
            Loc("Hastane Ana Poliklinik", "İstanbul", "Üsküdar", "Altunizade Mah. Kuşbakışı Cad. No:8", "2. Kat"), Loc("Alsancak Tıp Merkezi", "İzmir", "Konak", "Alsancak Mah. Şair Eşref Bul. No:25", "3. Kat")),
        Doc("Fatma", "Koç", "Uzm. Dr.", "Female", "Çocuk Sağlığı", "Tıp Doktoru, Çocuk Sağlığı ve Hastalıkları Uzmanı",
            "Bebek ve çocuk büyüme gelişme takibi, aşı danışmanlığı ve enfeksiyon hastalıkları alanında çalışır. Ebeveynleri bilgilendirmeye zaman ayırır.",
            ["Bebek Takibi", "Aşı Danışmanlığı", "Büyüme Gelişme", "Alerji"],
            Edu("Ankara Üniversitesi Tıp Fakültesi", "Tıp", 2001, 2007), Edu("Gazi Üniversitesi", "Çocuk Sağlığı ve Hastalıkları Uzmanlığı", 2007, 2012),
            Exp("Çocuk Hastanesi", "Uzman Doktor", 2012, 2018), Exp("Hastane Çocuk Kliniği", "Uzman Doktor", 2018, null),
            [Award("Çocuk Dostu Hekim Ödülü", 2021, "Çocuk Sağlığı Derneği")],
            Loc("Çankaya Sağlık Merkezi", "Ankara", "Çankaya", "Oran Mah. Park Cad. No:19", "Zemin Kat")),
        Doc("Emre", "Kurt", "Uzm. Dr.", "Male", "Çocuk Sağlığı", "Tıp Doktoru, Çocuk Sağlığı ve Hastalıkları Uzmanı",
            "Çocuklarda solunum yolu hastalıkları ve astım takibinde deneyimlidir. Aileyle birlikte uzun vadeli bir takip planı oluşturur.",
            ["Çocuk Astım Takibi", "Solunum Fonksiyon Testi", "Bağışıklama", "Yenidoğan Muayenesi"],
            Edu("Ege Üniversitesi Tıp Fakültesi", "Tıp", 2005, 2011), Edu("Ege Üniversitesi", "Çocuk Sağlığı ve Hastalıkları Uzmanlığı", 2011, 2016),
            Exp("Bölge Çocuk Hastanesi", "Uzman Doktor", 2016, 2022), Exp("Hastane Çocuk Kliniği", "Uzman Doktor", 2022, null),
            [],
            Loc("Karşıyaka Poliklinik", "İzmir", "Karşıyaka", "Mavişehir Mah. Anadolu Cad. No:5", "2. Kat")),
        Doc("Gizem", "Polat", "Doç. Dr.", "Female", "Dermatoloji", "Tıp Doktoru, Deri ve Zührevi Hastalıklar Doçenti",
            "Akne, egzama, sedef hastalığı ve cilt kanseri taraması konularında çalışır. Dermatoskopi ile benek takibi yapar.",
            ["Dermatoskopi", "Akne Tedavisi", "Egzama ve Sedef", "Cilt Kanseri Taraması"],
            Edu("Hacettepe Üniversitesi Tıp Fakültesi", "Tıp", 2000, 2006), Edu("Ankara Üniversitesi", "Deri ve Zührevi Hastalıklar Uzmanlığı", 2006, 2011),
            Exp("Üniversite Hastanesi", "Öğretim Üyesi", 2016, null), Exp("Devlet Hastanesi", "Uzman Doktor", 2011, 2016),
            [Award("Dermatoloji Araştırma Ödülü", 2015, "Türk Dermatoloji Derneği")],
            Loc("Hastane Ana Poliklinik", "İstanbul", "Kadıköy", "Fenerbahçe Mah. Bağdat Cad. No:150", "4. Kat"), Loc("Hastane Ankara Şubesi", "Ankara", "Çankaya", "Gaziosmanpaşa Mah. Tahran Cad. No:7", "2. Kat")),
    ];

    public static readonly PatientSeed[] Patients =
    [
        new("Can", "Yıldız", "hasta1@hastane.local", "Göğüste sıkışma hissi ve çarpıntı şikâyeti", "Tansiyon kontrolü", "Rutin kontrol muayenesi",
            "Çok ilgili ve açıklayıcı bir muayeneydi, tüm sorularımı sabırla yanıtladı. Teşekkürler.", 5),
        new("Merve", "Aksoy", "hasta2@hastane.local", "Sık tekrarlayan baş ağrısı", "Uykusuzluk şikâyeti", "Kontrol randevusu",
            "Randevu saatine sadık kalındı, muayene ayrıntılıydı. Tedavi planı net anlatıldı.", 4),
        new("Oğuz", "Erdem", "hasta3@hastane.local", "Dizde ağrı ve şişlik", "Omuzda hareket kısıtlılığı", "Fizik tedavi sonrası kontrol",
            "Sorunumu hızlıca tespit etti ve seçenekleri dürüstçe anlattı. Memnun kaldım.", 5),
        new("Ebru", "Güneş", "hasta4@hastane.local", "Cilt kızarıklığı ve kaşıntı", "Göz kuruluğu ve yanma", "Yıllık check-up",
            "Muayene güler yüzlü ve düzenliydi, bekleme süresi kısaydı. Tavsiye ederim.", 4),
        new("Kerem", "Tan", "hasta5@hastane.local", "Çocukta ateş ve öksürük", "Diş eti kanaması", "Aşı takvimi danışmanlığı",
            "Çocuğumla çok iyi iletişim kurdu, endişelerimizi giderdi. Çok teşekkür ederiz.", 5),
        new("Naz", "Yalçın", "hasta6@hastane.local", "Boyunda ağrı ve uyuşma", "Şeker ölçümü ve diyet danışmanlığı", "Genel sağlık kontrolü",
            "Genel olarak iyiydi ancak muayene beklediğimden kısa sürdü. Yine de önerileri işe yaradı.", 3),
    ];

    public static readonly MedicineSeed[][] PrescriptionSets =
    [
        [new("Parol 500 mg Tablet", "1 tablet", "Günde 3 kez", 5, "Tok karnına, bol su ile alınız."), new("Augmentin BID 1000 mg", "1 tablet", "Günde 2 kez", 7, "Yemeklerden hemen sonra kullanınız."), new("Majezik 100 mg Film Tablet", "1 tablet", "Günde 2 kez", 5, "Ağrı olduğunda, tok karnına alınız.")],
        [new("Lansor 30 mg Kapsül", "1 kapsül", "Günde 1 kez", 14, "Sabah aç karnına, kahvaltıdan 30 dakika önce alınız."), new("Beloc ZOK 50 mg Tablet", "1 tablet", "Günde 1 kez", 30, "Her gün aynı saatte alınız; çiğnemeyiniz."), new("Cardura 4 mg Tablet", "1 tablet", "Günde 1 kez", 30, "Akşam yatmadan önce alınız.")],
        [new("Nurofen 400 mg Tablet", "1 tablet", "Günde 2 kez", 5, "Yemekle birlikte alınız."), new("Myolastan 50 mg Tablet", "1 tablet", "Günde 1 kez", 7, "Akşam alınız, kullanım sonrası araç kullanmayınız."), new("Voltaren Emulgel", "Ağrılı bölgeye ince tabaka", "Günde 3 kez", 10, "Hafifçe masaj yaparak uygulayınız.")],
    ];

    public static string Ascii(string text)
    {
        var mapped = new StringBuilder();
        foreach (var c in text)
        {
            mapped.Append(c switch
            {
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'ı' or 'İ' => 'i',
                'ö' or 'Ö' => 'o',
                'ş' or 'Ş' => 's',
                'ü' or 'Ü' => 'u',
                _ => c,
            });
        }

        var normalized = mapped.ToString().Normalize(NormalizationForm.FormD);
        var clean = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) clean.Append(c);
        }

        return clean.ToString().ToLowerInvariant().Replace(' ', '-');
    }

    private static DoctorSeed Doc(
        string first, string last, string title, string gender, string branch, string degrees, string bio, string[] services,
        EducationSeed e1, EducationSeed e2, ExperienceSeed x1, ExperienceSeed x2, AwardSeed[] awards, params LocationSeed[] locations)
        => new(first, last, title, gender, branch, degrees, bio, services, [e1, e2], [x1, x2], awards, locations);

    private static EducationSeed Edu(string school, string department, int start, int end) => new(school, department, start, end);

    private static ExperienceSeed Exp(string place, string position, int start, int? end) => new(place, position, start, end);

    private static AwardSeed Award(string title, int year, string description) => new(title, year, description);

    private static LocationSeed Loc(string clinic, string city, string district, string address, string floor)
        => new(clinic, city, district, address, floor);
}
