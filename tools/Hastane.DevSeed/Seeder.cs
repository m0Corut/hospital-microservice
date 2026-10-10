namespace Hastane.DevSeed;

// Adımlar sırayla çalışır; her adım idempotenttir (var olanı atlar). Bir hata yalnız o kaydı atlatır, araç devam eder.
public sealed class Seeder(GatewayApi api, SeedOptions options, Report report)
{
    private const string StepBranch = "1 Branşlar";
    private const string StepDoctor = "2 Doktorlar";
    private const string StepDoctorUser = "3 Doktor hesapları";
    private const string StepPatient = "4 Hastalar";
    private const string StepAppointment = "5 Randevular";
    private const string StepStatus = "6 Randevu durumları";
    private const string StepPayment = "7 Ödemeler";
    private const string StepPrescription = "8 Reçeteler";
    private const string StepReview = "9 Yorumlar";

    // Türkiye sabit UTC+3 (yaz saati yok): "yarın" Appointment servisinin saat dilimindeki takvim gününe denk gelsin.
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    private readonly Dictionary<string, string> _branchIds = [];
    private readonly string[] _doctorIds = new string[SeedData.Doctors.Length];
    private readonly Dictionary<string, string> _doctorTokens = [];
    private readonly Dictionary<string, DoctorDetail> _doctorDetails = [];
    private readonly List<PatientSession> _patients = [];
    private string _adminToken = string.Empty;

    private sealed record PatientSession(int Index, PatientSeed Seed, string UserId, string Token);

    // false: Admin girişi yapılamadı (sonraki adımlar çalışmadı).
    public async Task<bool> RunAsync(CancellationToken ct)
    {
        Console.WriteLine($"Gateway: {options.Gateway}");
        Console.WriteLine("Admin girişi...");
        var login = await api.LoginAsync(options.AdminEmail, options.AdminPassword, ct);
        if (!login.Ok || login.Value is null)
        {
            var hint = login.Status switch
            {
                0 => "Gateway'e ulaşılamadı; servisler çalışıyor mu?",
                401 => "E-posta/şifre hatalı ya da Admin hesabı yok (Identity'de Seed:AdminEmail/Seed:AdminPassword tanımlı olmalı).",
                423 => "Hesap geçici olarak kilitli.",
                _ => "Beklenmeyen yanıt.",
            };
            report.Error("0 Admin girişi", options.AdminEmail, $"{login} - {hint}");
            return false;
        }

        if (!login.Value.User.Roles.Contains("Admin"))
            report.Note("Uyarı: giriş yapılan hesapta Admin rolü görünmüyor; Admin uçları reddedilebilir.");
        _adminToken = login.Value.AccessToken;

        await SeedBranchesAsync(ct);
        await SeedDoctorsAsync(ct);
        await SeedDoctorUsersAsync(ct);
        await SeedPatientsAsync(ct);
        await SeedAppointmentsAsync(ct);
        await UpdateStatusesAsync(ct);
        await SeedPaymentsAsync(ct);
        await SeedPrescriptionsAsync(ct);
        await SeedReviewsAsync(ct);
        return true;
    }

    // ---- 2: Branşlar ----
    private async Task SeedBranchesAsync(CancellationToken ct)
    {
        Console.WriteLine("Branşlar...");
        var existing = await api.ListBranchesAsync(ct);
        if (!existing.Ok || existing.Value is null)
        {
            report.Error(StepBranch, "liste", existing);
            return;
        }

        foreach (var branch in existing.Value) _branchIds[branch.Name] = branch.Id;

        foreach (var seed in SeedData.Branches)
        {
            if (_branchIds.ContainsKey(seed.Name))
            {
                report.Skipped(StepBranch);
                continue;
            }

            var created = await api.CreateBranchAsync(_adminToken, new { name = seed.Name, description = seed.Description, icon = seed.Icon }, ct);
            if (created.Ok && created.Value is not null)
            {
                _branchIds[seed.Name] = created.Value.Id;
                report.Created(StepBranch);
            }
            else
            {
                report.Error(StepBranch, seed.Name, created);
            }
        }
    }

    // ---- 3: Doktorlar ----
    private async Task SeedDoctorsAsync(CancellationToken ct)
    {
        Console.WriteLine("Doktorlar...");
        var existing = await api.ListDoctorsAsync(ct);
        if (!existing.Ok || existing.Value is null)
        {
            report.Error(StepDoctor, "liste", existing);
            return;
        }

        for (var i = 0; i < SeedData.Doctors.Length; i++)
        {
            var seed = SeedData.Doctors[i];
            var match = existing.Value.FirstOrDefault(d => d.FirstName == seed.FirstName && d.LastName == seed.LastName);
            if (match is not null)
            {
                _doctorIds[i] = match.Id;
                report.Skipped(StepDoctor);
                continue;
            }

            if (!_branchIds.TryGetValue(seed.Branch, out var branchId))
            {
                report.Error(StepDoctor, seed.FullName, $"Branş bulunamadı: {seed.Branch}");
                continue;
            }

            var created = await api.CreateDoctorAsync(_adminToken, BuildDoctorBody(i, seed, branchId), ct);
            if (created.Ok && created.Value is not null)
            {
                _doctorIds[i] = created.Value.Id;
                report.Created(StepDoctor);
            }
            else
            {
                report.Error(StepDoctor, seed.FullName, created);
            }
        }
    }

    private object BuildDoctorBody(int index, DoctorSeed seed, string branchId)
    {
        var number = index + 1;
        var slug = SeedData.Ascii(seed.FullName);
        var gallery = Enumerable.Range(0, 2 + (index % 2)) // 2 ya da 3 görsel
            .Select(g => $"{options.WebBase}/img/clinics/clinic-{(index + g) % 6 + 1:00}.svg")
            .ToArray();

        var locations = seed.Locations.Select((loc, locIndex) => new
        {
            clinicName = loc.ClinicName,
            city = loc.City,
            district = loc.District,
            address = loc.Address,
            floor = loc.Floor,
            // 400 - 1500 TRY arası: doktor sırasına göre kademeli, ikinci konum biraz daha pahalı.
            hourlyFee = 400m + (index * 85m) + (locIndex * 100m),
            currency = "TRY",
            schedule = locIndex == 0 ? BuildSchedule(saturday: index % 3 == 0) : [],
        }).ToArray();

        return new
        {
            firstName = seed.FirstName,
            lastName = seed.LastName,
            title = seed.Title,
            gender = seed.Gender,
            degrees = seed.Degrees,
            branchId,
            biography = seed.Biography,
            imageUrl = $"/img/doctors/avatar-{number:00}.svg",
            phone = $"+90 212 555 01{number:00}",
            email = seed.Email,
            services = seed.Services,
            gallery,
            socialLinks = new
            {
                facebook = $"https://www.facebook.com/{slug}.example",
                x = $"https://x.com/{slug}_example",
                instagram = $"https://www.instagram.com/{slug}.example",
                linkedIn = $"https://www.linkedin.com/in/{slug}-example",
                pinterest = (string?)null,
                youTube = index % 4 == 0 ? $"https://www.youtube.com/@{slug}-example" : null,
            },
            educations = seed.Educations.Select(e => new { school = e.School, department = e.Department, startYear = e.StartYear, endYear = e.EndYear }).ToArray(),
            experiences = seed.Experiences.Select(e => new { place = e.Place, position = e.Position, startYear = e.StartYear, endYear = e.EndYear }).ToArray(),
            awards = seed.Awards.Select(a => new { title = a.Title, year = a.Year, description = a.Description }).ToArray(),
            clinicLocations = locations,
        };
    }

    // Pazartesi-Cuma 09:00-12:00 ve 13:00-17:00; saturday ise Cumartesi 10:00-14:00 de eklenir. Dilim 30 dk.
    private static object[] BuildSchedule(bool saturday)
    {
        var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };
        var slots = new List<object>();
        foreach (var day in days)
        {
            slots.Add(new { dayOfWeek = day, startTime = "09:00", endTime = "12:00", slotMinutes = 30 });
            slots.Add(new { dayOfWeek = day, startTime = "13:00", endTime = "17:00", slotMinutes = 30 });
        }

        if (saturday) slots.Add(new { dayOfWeek = "Saturday", startTime = "10:00", endTime = "14:00", slotMinutes = 30 });
        return [.. slots];
    }

    // ---- 4: Doktor hesapları ----
    private async Task SeedDoctorUsersAsync(CancellationToken ct)
    {
        Console.WriteLine("Doktor hesapları...");
        for (var i = 0; i < SeedData.Doctors.Length; i++)
        {
            var seed = SeedData.Doctors[i];
            var doctorId = _doctorIds[i];
            if (string.IsNullOrEmpty(doctorId))
            {
                report.Error(StepDoctorUser, seed.Email, "Doktor kaydı yok, hesap bağlanamadı.");
                continue;
            }

            var ensured = await EnsureUserAsync(StepDoctorUser, seed.FirstName, seed.LastName, seed.Email, ct);
            if (ensured is null) continue;

            var user = ensured.Value.User;
            var changed = ensured.Value.Created;

            if (!user.Roles.Contains("Doctor"))
            {
                var added = await api.AddRoleAsync(_adminToken, user.Id, "Doctor", ct);
                if (!added.Ok || added.Value is null)
                {
                    report.Error(StepDoctorUser, $"{seed.Email} Doctor rolü", added);
                    continue;
                }

                user = added.Value;
                changed = true;
            }

            if (user.Roles.Contains("Patient"))
            {
                var removed = await api.RemoveRoleAsync(_adminToken, user.Id, "Patient", ct);
                if (removed.Ok && removed.Value is not null)
                {
                    user = removed.Value;
                    changed = true;
                }
                else if (removed.Status is 404 or 405)
                {
                    report.Note($"{seed.Email}: Patient rolü kaldırma ucu yok/uygulanamadı ({removed.Status}); atlandı.");
                }
                else
                {
                    report.Error(StepDoctorUser, $"{seed.Email} Patient rolünü kaldır", removed);
                }
            }

            if (user.DoctorId != doctorId)
            {
                var linked = await api.LinkDoctorAsync(_adminToken, user.Id, doctorId, ct);
                if (!linked.Ok)
                {
                    report.Error(StepDoctorUser, $"{seed.Email} doktor bağı", linked);
                    continue;
                }

                changed = true;
            }

            if (changed) report.Created(StepDoctorUser);
            else report.Skipped(StepDoctorUser);
        }
    }

    // ---- 5: Hastalar ----
    private async Task SeedPatientsAsync(CancellationToken ct)
    {
        Console.WriteLine("Hastalar...");
        for (var i = 0; i < SeedData.Patients.Length; i++)
        {
            var seed = SeedData.Patients[i];
            var ensured = await EnsureUserAsync(StepPatient, seed.FirstName, seed.LastName, seed.Email, ct);
            if (ensured is null) continue;

            if (ensured.Value.Created) report.Created(StepPatient);
            else report.Skipped(StepPatient);

            var login = await api.LoginAsync(seed.Email, options.SeedPassword, ct);
            if (!login.Ok || login.Value is null)
            {
                report.Error(StepPatient, $"{seed.Email} giriş", login);
                continue;
            }

            _patients.Add(new PatientSession(i, seed, login.Value.User.Id, login.Value.AccessToken));
        }
    }

    // Kayıt dener; e-posta kayıtlıysa (409) giriş yapıp mevcut kullanıcıyı döndürür. null: hata yazıldı.
    private async Task<(UserInfo User, bool Created)?> EnsureUserAsync(
        string step, string firstName, string lastName, string email, CancellationToken ct)
    {
        var registered = await api.RegisterAsync(firstName, lastName, email, options.SeedPassword, ct);
        if (registered.Ok && registered.Value is not null) return (registered.Value, true);

        if (!registered.IsConflict)
        {
            report.Error(step, $"{email} kayıt", registered);
            return null;
        }

        var login = await api.LoginAsync(email, options.SeedPassword, ct);
        if (!login.Ok || login.Value is null)
        {
            report.Error(step, $"{email} giriş (hesap var ama şifre farklı olabilir)", login);
            return null;
        }

        return (login.Value.User, false);
    }

    // ---- 6: Randevular ----
    private async Task SeedAppointmentsAsync(CancellationToken ct)
    {
        Console.WriteLine("Randevular...");
        var today = DateOnly.FromDateTime((DateTime.UtcNow + TurkeyOffset).Date);

        foreach (var patient in _patients)
        {
            var target = 2 + (patient.Index % 2); // 2 ya da 3
            var existing = await api.ListMyAppointmentsAsync(patient.Token, ct);
            if (!existing.Ok || existing.Value is null)
            {
                report.Error(StepAppointment, $"{patient.Seed.Email} liste", existing);
                continue;
            }

            if (existing.Value.Count >= target)
            {
                report.Skipped(StepAppointment, existing.Value.Count);
                continue;
            }

            report.Skipped(StepAppointment, existing.Value.Count);
            var complaints = new[] { patient.Seed.Complaint1, patient.Seed.Complaint2, patient.Seed.Complaint3 };
            var usedDoctors = existing.Value.Select(a => a.DoctorId).ToHashSet();

            for (var n = existing.Value.Count; n < target; n++)
            {
                var doctorId = PickDoctorId(patient.Index, n, usedDoctors);
                if (doctorId is null)
                {
                    report.Error(StepAppointment, patient.Seed.Email, "Randevu için doktor kaydı yok.");
                    break;
                }

                usedDoctors.Add(doctorId);
                await BookFirstFreeSlotAsync(patient, doctorId, complaints[n % complaints.Length], today, n, ct);
            }
        }
    }

    // Hastanın her randevusu farklı doktora gitsin (yorum/reçete çeşitliliği); sıra hasta ve sıra no'ya göre kayar.
    private string? PickDoctorId(int patientIndex, int appointmentNo, HashSet<string> used)
    {
        for (var shift = 0; shift < _doctorIds.Length; shift++)
        {
            var id = _doctorIds[(patientIndex * 2 + appointmentNo + shift) % _doctorIds.Length];
            if (!string.IsNullOrEmpty(id) && !used.Contains(id)) return id;
        }

        return null;
    }

    private async Task BookFirstFreeSlotAsync(
        PatientSession patient, string doctorId, string note, DateOnly today, int appointmentNo, CancellationToken ct)
    {
        var branchId = (await GetDoctorDetailAsync(doctorId, ct))?.BranchId;
        if (branchId is null)
        {
            report.Error(StepAppointment, patient.Seed.Email, $"Doktor ayrıntısı alınamadı ({doctorId}).");
            return;
        }

        for (var offset = 1; offset <= 10; offset++)
        {
            var slots = await api.GetAvailabilityAsync(patient.Token, doctorId, today.AddDays(offset), ct);
            if (!slots.Ok || slots.Value is null)
            {
                report.Error(StepAppointment, $"{patient.Seed.Email} uygunluk ({doctorId})", slots);
                return;
            }

            if (slots.Value.Count == 0) continue;

            // Aynı saate yığılmasın: sıra no kadar ileriden başla, o dilim çakışırsa sonrakini dene (en çok 3).
            var start = Math.Min(appointmentNo * 2, slots.Value.Count - 1);
            foreach (var slot in slots.Value.Skip(start).Take(3))
            {
                var booked = await api.CreateAppointmentAsync(patient.Token, doctorId, branchId, slot.Start, note, ct);
                if (booked.Ok)
                {
                    report.Created(StepAppointment);
                    return;
                }

                if (!booked.IsConflict)
                {
                    report.Error(StepAppointment, $"{patient.Seed.Email} randevu ({doctorId})", booked);
                    return;
                }
            }
        }

        report.Error(StepAppointment, patient.Seed.Email, $"10 gün içinde boş slot bulunamadı ({doctorId}).");
    }

    // ---- 7: Durumlar ----
    // Konum sırasına göre (tarihe göre sıralı liste): 1. Completed, 2. (bazı hastalarda hasta iptali, yoksa Confirmed), 3. Pending kalır.
    private async Task UpdateStatusesAsync(CancellationToken ct)
    {
        Console.WriteLine("Randevu durumları...");
        foreach (var patient in _patients)
        {
            var list = await api.ListMyAppointmentsAsync(patient.Token, ct);
            if (!list.Ok || list.Value is null)
            {
                report.Error(StepStatus, $"{patient.Seed.Email} liste", list);
                continue;
            }

            for (var pos = 0; pos < list.Value.Count; pos++)
            {
                var appointment = list.Value[pos];
                var patientCancels = pos == 1 && patient.Index % 3 == 0;
                var target = pos switch
                {
                    0 => "Completed",
                    1 => patientCancels ? "Cancelled" : "Confirmed",
                    _ => "Pending",
                };

                await ApplyStatusAsync(patient, appointment, target, patientCancels, ct);
            }
        }
    }

    private async Task ApplyStatusAsync(PatientSession patient, AppointmentInfo appointment, string target, bool patientCancels, CancellationToken ct)
    {
        if (appointment.Status == target || target == "Pending" || appointment.Status is "Completed" or "Cancelled")
        {
            report.Skipped(StepStatus);
            return;
        }

        if (patientCancels)
        {
            var cancelled = await api.SetStatusAsync(patient.Token, appointment.Id, "Cancelled", ct);
            if (cancelled.Ok) report.Created(StepStatus);
            else report.Error(StepStatus, $"{appointment.Id} iptal (hasta)", cancelled);
            return;
        }

        var doctorToken = await GetDoctorTokenAsync(appointment.DoctorId, ct);
        if (doctorToken is null)
        {
            report.Error(StepStatus, appointment.Id.ToString(), "Doktor hesabıyla giriş yapılamadı.");
            return;
        }

        if (appointment.Status == "Pending")
        {
            var confirmed = await api.SetStatusAsync(doctorToken, appointment.Id, "Confirmed", ct);
            if (!confirmed.Ok)
            {
                report.Error(StepStatus, $"{appointment.Id} Confirmed", confirmed);
                return;
            }
        }

        if (target == "Completed")
        {
            var completed = await api.SetStatusAsync(doctorToken, appointment.Id, "Completed", ct);
            if (!completed.Ok)
            {
                report.Error(StepStatus, $"{appointment.Id} Completed", completed);
                return;
            }
        }

        report.Created(StepStatus);
    }

    // ---- 8: Ödemeler ----
    // Çift sıra numaralı hastaların Confirmed/Completed randevuları (yaklaşık yarısı). Anahtar randevudan türetilir: tekrar çalıştırmada aynı ödeme geri döner.
    private async Task SeedPaymentsAsync(CancellationToken ct)
    {
        Console.WriteLine("Ödemeler...");
        var expYear = DateTime.UtcNow.Year + 3;
        foreach (var patient in _patients.Where(p => p.Index % 2 == 0))
        {
            var list = await api.ListMyAppointmentsAsync(patient.Token, ct);
            if (!list.Ok || list.Value is null)
            {
                report.Error(StepPayment, $"{patient.Seed.Email} liste", list);
                continue;
            }

            foreach (var appointment in list.Value.Where(a => a.Status is "Confirmed" or "Completed"))
            {
                var detail = await GetDoctorDetailAsync(appointment.DoctorId, ct);
                if (detail?.MinFee is not { } fee || string.IsNullOrWhiteSpace(detail.Currency))
                {
                    report.Error(StepPayment, appointment.Id.ToString(), "Doktorun ücreti okunamadı.");
                    continue;
                }

                var body = new
                {
                    appointmentId = appointment.Id,
                    amount = fee,
                    currency = detail.Currency,
                    description = "Muayene ve hizmet bedeli",
                    card = new
                    {
                        number = "4242424242424242",
                        expMonth = 12,
                        expYear,
                        cvv = "123",
                        holderName = $"{patient.Seed.FirstName} {patient.Seed.LastName}".ToUpperInvariant(),
                    },
                };

                var paid = await api.CreatePaymentAsync(patient.Token, "seed-pay-" + appointment.Id.ToString("N"), body, ct);
                if (paid.Status == 201) report.Created(StepPayment);
                else if (paid.Ok) report.Skipped(StepPayment); // aynı anahtarla önceki ödeme geri döndü
                else if (paid.IsConflict && (paid.Error ?? string.Empty).Contains("zaten ödeme", StringComparison.OrdinalIgnoreCase))
                    report.Skipped(StepPayment);
                else report.Error(StepPayment, $"{appointment.Id}", paid);
            }
        }
    }

    // ---- 9: Reçeteler ----
    private async Task SeedPrescriptionsAsync(CancellationToken ct)
    {
        Console.WriteLine("Reçeteler...");
        var counter = 0;
        foreach (var patient in _patients)
        {
            var list = await api.ListMyAppointmentsAsync(patient.Token, ct);
            if (!list.Ok || list.Value is null)
            {
                report.Error(StepPrescription, $"{patient.Seed.Email} liste", list);
                continue;
            }

            foreach (var appointment in list.Value.Where(a => a.Status == "Completed"))
            {
                var doctorToken = await GetDoctorTokenAsync(appointment.DoctorId, ct);
                if (doctorToken is null)
                {
                    report.Error(StepPrescription, appointment.Id.ToString(), "Doktor hesabıyla giriş yapılamadı.");
                    continue;
                }

                var existing = await api.ListPrescriptionsByAppointmentAsync(doctorToken, appointment.Id, ct);
                if (!existing.Ok || existing.Value is null)
                {
                    report.Error(StepPrescription, $"{appointment.Id} liste", existing);
                    continue;
                }

                if (existing.Value.Count > 0)
                {
                    report.Skipped(StepPrescription);
                    continue;
                }

                var medicines = SeedData.PrescriptionSets[counter++ % SeedData.PrescriptionSets.Length];
                var body = new
                {
                    appointmentId = appointment.Id,
                    patientId = appointment.PatientId,
                    notes = "Kullanım talimatına uyunuz; şikâyetler sürerse kontrole geliniz.",
                    items = medicines.Select(m => new
                    {
                        medicineName = m.Name,
                        dosage = m.Dosage,
                        frequency = m.Frequency,
                        durationDays = m.Days,
                        instructions = m.Instructions,
                    }).ToArray(),
                };

                var created = await api.CreatePrescriptionAsync(doctorToken, body, ct);
                if (created.Ok) report.Created(StepPrescription);
                else report.Error(StepPrescription, appointment.Id.ToString(), created);
            }
        }
    }

    // ---- 10: Yorumlar ----
    private async Task SeedReviewsAsync(CancellationToken ct)
    {
        Console.WriteLine("Yorumlar...");
        foreach (var patient in _patients)
        {
            var list = await api.ListMyAppointmentsAsync(patient.Token, ct);
            if (!list.Ok || list.Value is null)
            {
                report.Error(StepReview, $"{patient.Seed.Email} liste", list);
                continue;
            }

            foreach (var doctorId in list.Value.Where(a => a.Status == "Completed").Select(a => a.DoctorId).Distinct())
            {
                var existing = await api.ListReviewsByDoctorAsync(doctorId, ct);
                if (!existing.Ok || existing.Value is null)
                {
                    report.Error(StepReview, $"{doctorId} liste", existing);
                    continue;
                }

                // Yorum sahibi liste yanıtında yok; her hastanın metni özgün olduğu için metinle eşleştiriyoruz.
                if (existing.Value.Any(r => r.Comment == patient.Seed.Review))
                {
                    report.Skipped(StepReview);
                    continue;
                }

                var created = await api.CreateReviewAsync(patient.Token, doctorId, patient.Seed.Rating, patient.Seed.Review, ct);
                if (created.Ok) report.Created(StepReview);
                else report.Error(StepReview, $"{patient.Seed.Email} -> {doctorId}", created);
            }
        }
    }

    // ---- yardımcılar ----
    private async Task<DoctorDetail?> GetDoctorDetailAsync(string doctorId, CancellationToken ct)
    {
        if (_doctorDetails.TryGetValue(doctorId, out var cached)) return cached;
        var result = await api.GetDoctorAsync(_adminToken, doctorId, ct);
        if (!result.Ok || result.Value is null) return null;
        _doctorDetails[doctorId] = result.Value;
        return result.Value;
    }

    // Doktor hesabıyla giriş (doctor_id claim'i bağlandıktan SONRA alınmış token gerekir; bu yüzden adımın içinde, tembel).
    private async Task<string?> GetDoctorTokenAsync(string doctorId, CancellationToken ct)
    {
        if (_doctorTokens.TryGetValue(doctorId, out var cached)) return cached;

        var index = Array.IndexOf(_doctorIds, doctorId);
        if (index < 0) return null;

        var login = await api.LoginAsync(SeedData.Doctors[index].Email, options.SeedPassword, ct);
        if (!login.Ok || login.Value is null) return null;

        _doctorTokens[doctorId] = login.Value.AccessToken;
        return login.Value.AccessToken;
    }
}
