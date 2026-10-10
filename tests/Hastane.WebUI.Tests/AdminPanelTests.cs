using System.Text.Json;
using System.Net;
using static Hastane.WebUI.Tests.AdminPanelTestHelpers;

namespace Hastane.WebUI.Tests;

public class AdminAccessTests
{
    public static TheoryData<string> AdminPages() => new()
    {
        "/Admin", "/Admin/Branches", "/Admin/Doctors", "/Admin/Users", "/Admin/Roles",
        "/Admin/Appointments", "/Admin/Payments", "/Admin/Invoices", "/Admin/Prescriptions",
    };

    [Theory]
    [MemberData(nameof(AdminPages))]
    public async Task Anonymous_is_redirected_to_login(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", Location(response));
        Assert.Empty(host.Gateway.Requests); // anonim istek servise hiç ulaşmaz
    }

    [Theory]
    [MemberData(nameof(AdminPages))]
    public async Task Patient_and_doctor_get_403_and_no_gateway_call(string path)
    {
        foreach (var role in new[] { "Patient", "Doctor" })
        {
            await using var host = await WebUiTestHost.StartAsync();
            var client = await ClientAsync(host, role);
            var before = host.Gateway.Requests.Count;

            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("Yetkiniz yok", await HtmlAsync(response));
            Assert.Equal(before, host.Gateway.Requests.Count); // yetkisiz kullanıcı için servise hiç gidilmez
        }
    }

    [Fact]
    public async Task Patient_cannot_post_a_delete()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/branches", 200, Array.Empty<object>());
        var client = await ClientAsync(host, "Patient");

        var response = await PostAsync(host, client, "/", $"/Admin/Branches/Delete/{BranchId}"); // belirteç: düzen dosyasındaki çıkış formundan

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "DELETE");
    }

    [Fact]
    public async Task Home_links_to_every_section_and_shows_no_invented_counts()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var href in new[] { "/Admin/Branches", "/Admin/Doctors", "/Admin/Users", "/Admin/Roles", "/Admin/Appointments", "/Admin/Payments", "/Admin/Invoices", "/Admin/Prescriptions" })
            Assert.Contains($"href=\"{href}\"", html);
        Assert.Contains("yakında", html);
    }

    // Tüm yazma uçları antiforgery ister: belirteçsiz POST 400, servise hiçbir yazma gitmez.
    public static TheoryData<string> PostEndpoints() => new()
    {
        "/Admin/Branches/Create", "/Admin/Branches/Edit/br1", "/Admin/Branches/Delete/br1",
        "/Admin/Doctors/Create", "/Admin/Doctors/Edit/doc1", "/Admin/Doctors/Delete/doc1",
        "/Admin/Users/AddRole/user-9", "/Admin/Users/RemoveRole/user-9", "/Admin/Users/LinkDoctor/user-9",
        "/Admin/Roles/Create",
        $"/Admin/Appointments/Delete/{AppointmentId}", $"/Admin/Payments/Refund/{PaymentId}",
    };

    [Theory]
    [MemberData(nameof(PostEndpoints))]
    public async Task Every_post_requires_antiforgery_token(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["role"] = "Admin" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method is "POST" or "PUT" or "DELETE" or "PATCH" && r.Path != "/gateway/auth/login");
    }

    // Silme/iade GET ile yapılamaz: GET yalnız onay sayfasını gösterir, servise yazma gitmez.
    [Fact]
    public async Task Get_on_delete_and_refund_pages_never_writes()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/branches/{BranchId}", 200, Branch());
        host.Gateway.MapJson("GET", $"/gateway/doctors/{DoctorId}", 200, DoctorDetail());
        host.Gateway.MapJson("GET", $"/gateway/appointments/{AppointmentId}", 200, Appointment());
        host.Gateway.MapJson("GET", $"/gateway/payments/{PaymentId}", 200, Payment());
        var client = await AdminClientAsync(host);

        foreach (var path in new[]
        {
            $"/Admin/Branches/Delete/{BranchId}", $"/Admin/Doctors/Delete/{DoctorId}",
            $"/Admin/Appointments/Delete/{AppointmentId}", $"/Admin/Payments/Refund/{PaymentId}",
        })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("method=\"post\"", await response.Content.ReadAsStringAsync());
        }

        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method != "GET" && r.Path != "/gateway/auth/login");
    }

    [Fact]
    public async Task Unknown_record_shows_404_page()
    {
        await using var host = await WebUiTestHost.StartAsync(); // /gateway/branches/yok tanımsız -> sahte gateway 404
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Branches/Edit/yok");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Sayfa bulunamadı", await HtmlAsync(response));
    }

    [Fact]
    public async Task Service_down_shows_503_page_without_details()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/branches", 500);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Branches");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Servis şu an yanıt vermiyor", html);
        Assert.DoesNotContain("127.0.0.1", html);
    }
}

public class AdminBranchTests
{
    [Fact]
    public async Task List_shows_branches_with_actions()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { Branch(), Branch("br2", "Nöroloji") });
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Branches"));

        Assert.Contains("Kardiyoloji", html);
        Assert.Contains("Nöroloji", html);
        Assert.Contains("/Admin/Branches/Edit/br1", html);
        Assert.Contains("/Admin/Branches/Delete/br2", html);
        Assert.Equal("Bearer", Last(host, "GET", "/gateway/branches").Authorization.Split(' ')[0]);
    }

    [Fact]
    public async Task List_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/branches", 200, Array.Empty<object>());
        var client = await AdminClientAsync(host);

        Assert.Contains("Henüz branş yok", await HtmlAsync(await client.GetAsync("/Admin/Branches")));
    }

    [Fact]
    public async Task Create_posts_json_to_gateway_and_redirects()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/branches", 201, Branch("new1", "Dermatoloji"));
        var client = await AdminClientAsync(host);

        var response = await host.PostFormAsync(client, "/Admin/Branches/Create", new()
        {
            ["Name"] = "  Dermatoloji ",
            ["Description"] = "Cilt",
            ["Icon"] = "",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Branches", Location(response));
        var body = Json(Last(host, "POST", "/gateway/branches"));
        Assert.Equal("Dermatoloji", body.GetProperty("name").GetString());
        Assert.Equal("Cilt", body.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("icon").ValueKind);
    }

    [Fact]
    public async Task Create_shows_local_validation_without_calling_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var response = await host.PostFormAsync(client, "/Admin/Branches/Create", new() { ["Name"] = "" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-valmsg-replace=\"true\">Branş adı gerekli.", await HtmlAsync(response));
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "POST" && r.Path == "/gateway/branches");
    }

    [Fact]
    public async Task Create_api_400_is_shown_on_the_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/branches", 400, new
        {
            status = 400,
            title = "Doğrulama",
            errors = new Dictionary<string, string[]> { ["Name"] = ["API: bu ad kullanılamaz."] },
        });
        var client = await AdminClientAsync(host);

        var response = await host.PostFormAsync(client, "/Admin/Branches/Create", new() { ["Name"] = "X" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("API: bu ad kullanılamaz.", await HtmlAsync(response));
    }

    [Fact]
    public async Task Edit_loads_record_and_puts_with_id()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/branches/{BranchId}", 200, Branch());
        host.Gateway.MapStatus("PUT", $"/gateway/branches/{BranchId}", 204);
        var client = await AdminClientAsync(host);

        var page = await HtmlAsync(await client.GetAsync($"/Admin/Branches/Edit/{BranchId}"));
        Assert.Contains("value=\"Kardiyoloji\"", page);

        var response = await host.PostFormAsync(client, $"/Admin/Branches/Edit/{BranchId}", new() { ["Name"] = "Kardiyoloji 2" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var body = Json(Last(host, "PUT", $"/gateway/branches/{BranchId}"));
        Assert.Equal(BranchId, body.GetProperty("id").GetString());
        Assert.Equal("Kardiyoloji 2", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Delete_requires_confirmation_page_then_post_sends_delete()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/branches/{BranchId}", 200, Branch());
        host.Gateway.MapStatus("DELETE", $"/gateway/branches/{BranchId}", 204);
        var client = await AdminClientAsync(host);

        var confirm = await HtmlAsync(await client.GetAsync($"/Admin/Branches/Delete/{BranchId}"));
        Assert.Contains("emin misiniz", confirm);
        Assert.False(Called(host, "DELETE", $"/gateway/branches/{BranchId}"));

        var response = await host.PostFormAsync(client, $"/Admin/Branches/Delete/{BranchId}", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(Called(host, "DELETE", $"/gateway/branches/{BranchId}"));
    }
}

public class AdminDoctorTests
{
    private static Dictionary<string, string> FullDoctorForm() => new()
    {
        ["FirstName"] = "Ali",
        ["LastName"] = "Veli",
        ["Title"] = "Dr.",
        ["Gender"] = "Male",
        ["BranchId"] = BranchId,
        ["Degrees"] = "Uzman",
        ["Email"] = "ali@test.local",
        ["Phone"] = "0555",
        ["Biography"] = "Bio",
        ["ImageUrl"] = "https://x.test/a.jpg",
        ["Services[0]"] = "Muayene",
        ["Services[1]"] = "",
        ["Gallery[0]"] = "https://x.test/g1.jpg",
        ["SocialLinks.Facebook"] = "https://fb.test/a",
        ["Educations[0].School"] = "ODTÜ",
        ["Educations[0].Department"] = "Tıp",
        ["Educations[0].StartYear"] = "2000",
        ["Educations[0].EndYear"] = "2006",
        ["Experiences[0].Place"] = "Hastane A",
        ["Experiences[0].Position"] = "Asistan",
        ["Experiences[0].StartYear"] = "2006",
        ["Experiences[0].EndYear"] = "2010",
        ["Awards[0].Title"] = "Ödül 1",
        ["Awards[0].Year"] = "2012",
        ["Awards[0].Description"] = "Açıklama",
        ["ClinicLocations[0].ClinicName"] = "Merkez Klinik",
        ["ClinicLocations[0].City"] = "Ankara",
        ["ClinicLocations[0].District"] = "Çankaya",
        ["ClinicLocations[0].HourlyFee"] = "450,5",
        ["ClinicLocations[0].Currency"] = "TRY",
        ["ClinicLocations[0].Schedule[0].DayOfWeek"] = "Monday",
        ["ClinicLocations[0].Schedule[0].StartTime"] = "09:00",
        ["ClinicLocations[0].Schedule[0].EndTime"] = "12:00",
        ["ClinicLocations[0].Schedule[0].SlotMinutes"] = "30",
        ["ClinicLocations[0].Schedule[1].DayOfWeek"] = "Friday",
        ["ClinicLocations[0].Schedule[1].StartTime"] = "13:00",
        ["ClinicLocations[0].Schedule[1].EndTime"] = "17:00",
        ["ClinicLocations[0].Schedule[1].SlotMinutes"] = "20",
    };

    private static void MapBranches(WebUiTestHost host)
        => host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { Branch(), Branch("br2", "Nöroloji") });

    [Fact]
    public async Task List_uses_search_endpoint_with_filters_and_shows_branch_names()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapBranches(host);
        var query = MapCapturingQuery(host, "GET", "/gateway/doctors/search", 200,
            Paged([DoctorListItem()], page: 2, pageSize: 10, total: 25));
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Doctors?text=Ali&branchId=br1&page=2"));

        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("Kardiyoloji", html);
        Assert.Contains("Ankara", html);
        var q = Uri.UnescapeDataString(query()!);
        Assert.Contains("text=Ali", q);
        Assert.Contains("branchIds=br1", q);
        Assert.Contains("page=2", q);
        Assert.Contains("pageSize=10", q);
        Assert.Contains("/Admin/Doctors/Edit/doc1", html);
        Assert.Contains("page=3", html); // sayfalama: "Sonraki" bağlantısı süzgeçleri taşır
    }

    [Fact]
    public async Task List_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapBranches(host);
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, Paged([]));
        var client = await AdminClientAsync(host);

        Assert.Contains("Kayıt bulunamadı", await HtmlAsync(await client.GetAsync("/Admin/Doctors")));
    }

    [Fact]
    public async Task Create_form_has_branch_dropdown_from_branch_service()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapBranches(host);
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Doctors/Create"));

        Assert.Contains("<option value=\"br1\">Kardiyoloji</option>", html);
        Assert.Contains("<option value=\"br2\">Nöroloji</option>", html);
        Assert.Contains("name=\"command\" value=\"add:Educations\"", html);
        Assert.Contains("name=\"command\" value=\"add:ClinicLocations\"", html);
    }

    [Fact]
    public async Task Create_sends_full_nested_json_body()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapBranches(host);
        host.Gateway.MapJson("POST", "/gateway/doctors", 201, DoctorListItem());
        var client = await AdminClientAsync(host);

        var response = await host.PostFormAsync(client, "/Admin/Doctors/Create", FullDoctorForm());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Doctors", Location(response));
        var body = Json(Last(host, "POST", "/gateway/doctors"));
        Assert.False(body.TryGetProperty("id", out _)); // oluştururken Id gönderilmez
        Assert.Equal("Ali", body.GetProperty("firstName").GetString());
        Assert.Equal("Male", body.GetProperty("gender").GetString()); // enum metin
        Assert.Equal(BranchId, body.GetProperty("branchId").GetString());
        Assert.Equal(["Muayene"], body.GetProperty("services").EnumerateArray().Select(e => e.GetString()).ToArray()); // boş öğe atıldı
        Assert.Equal("https://x.test/g1.jpg", body.GetProperty("gallery")[0].GetString());
        Assert.Equal("https://fb.test/a", body.GetProperty("socialLinks").GetProperty("facebook").GetString());

        var education = body.GetProperty("educations")[0];
        Assert.Equal("ODTÜ", education.GetProperty("school").GetString());
        Assert.Equal(2000, education.GetProperty("startYear").GetInt32());
        Assert.Equal(2006, education.GetProperty("endYear").GetInt32());

        var experience = body.GetProperty("experiences")[0];
        Assert.Equal("Asistan", experience.GetProperty("position").GetString());
        Assert.Equal(2010, experience.GetProperty("endYear").GetInt32());

        var award = body.GetProperty("awards")[0];
        Assert.Equal("Ödül 1", award.GetProperty("title").GetString());
        Assert.Equal(2012, award.GetProperty("year").GetInt32());

        var clinic = body.GetProperty("clinicLocations")[0];
        Assert.Equal("Merkez Klinik", clinic.GetProperty("clinicName").GetString());
        Assert.Equal(450.5m, clinic.GetProperty("hourlyFee").GetDecimal());
        Assert.Equal("TRY", clinic.GetProperty("currency").GetString());
        var schedule = clinic.GetProperty("schedule");
        Assert.Equal(2, schedule.GetArrayLength());
        Assert.Equal("Monday", schedule[0].GetProperty("dayOfWeek").GetString());
        Assert.Equal("09:00", schedule[0].GetProperty("startTime").GetString());
        Assert.Equal("Friday", schedule[1].GetProperty("dayOfWeek").GetString());
        Assert.Equal(20, schedule[1].GetProperty("slotMinutes").GetInt32());
    }

    [Fact]
    public async Task Missing_required_fields_are_reported_locally_with_nested_field_errors()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapBranches(host);
        var client = await AdminClientAsync(host);

        var response = await host.PostFormAsync(client, "/Admin/Doctors/Create", new()
        {
            ["FirstName"] = "Ali",
            ["Educations[0].School"] = "",
        });
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-valmsg-replace=\"true\">Soyad gerekli.", html);
        Assert.Contains("data-valmsg-replace=\"true\">Branş seçin.", html);
        Assert.Contains("data-valmsg-replace=\"true\">Okul gerekli.", html);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "POST" && r.Path == "/gateway/doctors");
    }

    [Fact]
    public async Task Api_400_with_nested_key_lands_on_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapBranches(host);
        host.Gateway.MapJson("POST", "/gateway/doctors", 400, new
        {
            status = 400,
            title = "Doğrulama",
            errors = new Dictionary<string, string[]>
            {
                ["ClinicLocations[0].Currency"] = ["API: para birimi geçersiz."],
                ["Email"] = ["API: e-posta kullanımda."],
            },
        });
        var client = await AdminClientAsync(host);

        var response = await host.PostFormAsync(client, "/Admin/Doctors/Create", FullDoctorForm());
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("API: para birimi geçersiz.", html);
        Assert.Contains("API: e-posta kullanımda.", html);
        Assert.Contains("value=\"Merkez Klinik\"", html); // girilen değerler kaybolmaz
    }

    [Fact]
    public async Task Add_button_adds_list_row_without_calling_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapBranches(host);
        var client = await AdminClientAsync(host);
        var form = new Dictionary<string, string>
        {
            ["FirstName"] = "Ali",
            ["Educations[0].School"] = "ODTÜ",
            ["command"] = "add:Educations",
        };

        var html = await HtmlAsync(await host.PostFormAsync(client, "/Admin/Doctors/Create", form));

        Assert.Contains("name=\"Educations[0].School\"", html);
        Assert.Contains("value=\"ODTÜ\"", html);
        Assert.Contains("name=\"Educations[1].School\"", html);
        Assert.DoesNotContain("field-validation-error", html); // komut doğrulama çalıştırmaz
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "POST" && r.Path == "/gateway/doctors");
    }

    [Fact]
    public async Task Remove_button_removes_only_that_row()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapBranches(host);
        var client = await AdminClientAsync(host);
        var form = new Dictionary<string, string>
        {
            ["Educations[0].School"] = "Birinci",
            ["Educations[1].School"] = "Ikinci",
            ["Educations[2].School"] = "Ucuncu",
            ["command"] = "remove:Educations:1",
        };

        var html = await HtmlAsync(await host.PostFormAsync(client, "/Admin/Doctors/Create", form));

        Assert.Contains("value=\"Birinci\"", html);
        Assert.DoesNotContain("value=\"Ikinci\"", html);
        Assert.Contains("value=\"Ucuncu\"", html);
        Assert.Contains("name=\"Educations[1].School\"", html); // sıra yeniden numaralandı
        Assert.DoesNotContain("name=\"Educations[2].School\"", html);
    }

    [Fact]
    public async Task Schedule_slot_buttons_work_inside_a_clinic()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapBranches(host);
        var client = await AdminClientAsync(host);

        var added = await HtmlAsync(await host.PostFormAsync(client, "/Admin/Doctors/Create", new()
        {
            ["ClinicLocations[0].ClinicName"] = "K1",
            ["ClinicLocations[0].Schedule[0].StartTime"] = "09:00",
            ["command"] = "add:Schedule:0",
        }));
        Assert.Contains("name=\"ClinicLocations[0].Schedule[1].StartTime\"", added);

        var removed = await HtmlAsync(await host.PostFormAsync(client, "/Admin/Doctors/Create", new()
        {
            ["ClinicLocations[0].ClinicName"] = "K1",
            ["ClinicLocations[0].Schedule[0].StartTime"] = "09:00",
            ["ClinicLocations[0].Schedule[1].StartTime"] = "14:00",
            ["command"] = "remove:Schedule:0:0",
        }));
        Assert.Contains("value=\"14:00\"", removed);
        Assert.DoesNotContain("value=\"09:00\"", removed);
    }

    [Fact]
    public async Task Edit_prefills_nested_data_and_puts_body_with_id()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapBranches(host);
        host.Gateway.MapJson("GET", $"/gateway/doctors/{DoctorId}", 200, DoctorDetail());
        host.Gateway.MapStatus("PUT", $"/gateway/doctors/{DoctorId}", 204);
        var client = await AdminClientAsync(host);

        var page = await HtmlAsync(await client.GetAsync($"/Admin/Doctors/Edit/{DoctorId}"));
        Assert.Contains("value=\"ODTÜ\"", page);
        Assert.Contains("value=\"Merkez Klinik\"", page);
        Assert.Contains("value=\"450.5\"", page);
        Assert.Contains("value=\"09:00\"", page);
        Assert.Contains("<option selected=\"selected\" value=\"br1\">Kardiyoloji</option>", page);

        var response = await host.PostFormAsync(client, $"/Admin/Doctors/Edit/{DoctorId}", FullDoctorForm());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var body = Json(Last(host, "PUT", $"/gateway/doctors/{DoctorId}"));
        Assert.Equal(DoctorId, body.GetProperty("id").GetString());
        Assert.Equal("Veli", body.GetProperty("lastName").GetString());
        Assert.Equal(2, body.GetProperty("clinicLocations")[0].GetProperty("schedule").GetArrayLength());
    }

    [Fact]
    public async Task Delete_needs_post_after_confirmation()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/doctors/{DoctorId}", 200, DoctorDetail());
        host.Gateway.MapStatus("DELETE", $"/gateway/doctors/{DoctorId}", 204);
        var client = await AdminClientAsync(host);

        Assert.Contains("emin misiniz", await HtmlAsync(await client.GetAsync($"/Admin/Doctors/Delete/{DoctorId}")));
        Assert.False(Called(host, "DELETE", $"/gateway/doctors/{DoctorId}"));

        var response = await host.PostFormAsync(client, $"/Admin/Doctors/Delete/{DoctorId}", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(Called(host, "DELETE", $"/gateway/doctors/{DoctorId}"));
    }
}

public class AdminUserAndRoleTests
{
    private static void MapUserPage(WebUiTestHost host, object user)
    {
        host.Gateway.MapJson("GET", "/gateway/users/user-9", 200, user);
        host.Gateway.MapJson("GET", "/gateway/roles", 200, new[] { new { name = "Admin" }, new { name = "Doctor" }, new { name = "Patient" } });
        host.Gateway.MapJson("GET", "/gateway/doctors", 200, new[] { DoctorListItem() });
    }

    [Fact]
    public async Task Lookup_form_redirects_to_user_details()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Users?id=user-9");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Users/Details/user-9", Location(response));
    }

    [Fact]
    public async Task Lookup_page_explains_that_there_is_no_user_list()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Users"));

        Assert.Contains("kullanıcı listesi sunmaz", html);
    }

    [Fact]
    public async Task Details_shows_user_roles_and_addable_roles_only()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUserPage(host, User(["Patient"]));
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Users/Details/user-9"));

        Assert.Contains("Zeynep Kaya", html);
        Assert.Contains("zeynep@test.local", html);
        Assert.Contains("<option value=\"Doctor\">", html);
        Assert.DoesNotContain("<option value=\"Patient\">", html); // zaten sahip olduğu rol listede yok
        Assert.Contains("önce Doctor rolü olmalı", html); // Doctor değilse bağlama formu yok
    }

    [Fact]
    public async Task Unknown_user_shows_404_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Users/Details/yok");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Sayfa bulunamadı", await HtmlAsync(response));
    }

    [Fact]
    public async Task Add_role_posts_json_body()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUserPage(host, User(["Patient"]));
        host.Gateway.MapJson("POST", "/gateway/users/user-9/roles", 200, User(["Patient", "Doctor"]));
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin/Users/Details/user-9", "/Admin/Users/AddRole/user-9", new() { ["role"] = "Doctor" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Users/Details/user-9", Location(response));
        Assert.Equal("Doctor", Json(Last(host, "POST", "/gateway/users/user-9/roles")).GetProperty("role").GetString());
    }

    [Fact]
    public async Task Remove_role_sends_delete_to_role_path()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUserPage(host, User(["Patient", "Doctor"]));
        host.Gateway.MapJson("DELETE", "/gateway/users/user-9/roles/Doctor", 200, User(["Patient"]));
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin/Users/Details/user-9", "/Admin/Users/RemoveRole/user-9", new() { ["role"] = "Doctor" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(Called(host, "DELETE", "/gateway/users/user-9/roles/Doctor"));
    }

    [Fact]
    public async Task Removing_last_admin_shows_clear_conflict_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUserPage(host, User(["Admin"]));
        host.MapProblem("DELETE", "/gateway/users/user-9/roles/Admin", 409, "Sistemin son Admin'inin Admin rolü kaldırılamaz.");
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin/Users/Details/user-9", "/Admin/Users/RemoveRole/user-9", new() { ["role"] = "Admin" });
        var html = await HtmlAsync(await client.GetAsync(Location(response)!));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Sistemin son Admin'inin Admin rolü kaldırılamaz.", html);
        Assert.Contains("alert-warning", html);
    }

    [Fact]
    public async Task Link_doctor_puts_doctor_id_to_doctor_link()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUserPage(host, User(["Doctor"]));
        host.Gateway.MapJson("PUT", "/gateway/users/user-9/doctor-link", 200, User(["Doctor"], DoctorId));
        var client = await AdminClientAsync(host);

        var page = await HtmlAsync(await client.GetAsync("/Admin/Users/Details/user-9"));
        Assert.Contains("<option value=\"doc1\">Dr. Ali Veli</option>", page); // doktor listeden seçilir

        var response = await PostAsync(host, client, "/Admin/Users/Details/user-9", "/Admin/Users/LinkDoctor/user-9", new() { ["doctorId"] = DoctorId });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(DoctorId, Json(Last(host, "PUT", "/gateway/users/user-9/doctor-link")).GetProperty("doctorId").GetString());
    }

    [Fact]
    public async Task Link_doctor_conflict_message_is_shown()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUserPage(host, User(["Doctor"]));
        host.MapProblem("PUT", "/gateway/users/user-9/doctor-link", 409, "Bu doktor kaydı başka bir kullanıcıya bağlı.");
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin/Users/Details/user-9", "/Admin/Users/LinkDoctor/user-9", new() { ["doctorId"] = DoctorId });
        var html = await HtmlAsync(await client.GetAsync(Location(response)!));

        Assert.Contains("Bu doktor kaydı başka bir kullanıcıya bağlı.", html);
    }

    [Fact]
    public async Task Roles_page_lists_and_creates()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/roles", 200, new[] { new { name = "Admin" }, new { name = "Nurse" } });
        host.Gateway.MapJson("POST", "/gateway/roles", 201, new { name = "Pharmacist" });
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Roles"));
        Assert.Contains("Nurse", html);

        var response = await PostAsync(host, client, "/Admin/Roles", "/Admin/Roles/Create", new() { ["Name"] = "Pharmacist" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Pharmacist", Json(Last(host, "POST", "/gateway/roles")).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Duplicate_role_409_is_shown_on_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/roles", 200, new[] { new { name = "Admin" } });
        host.MapProblem("POST", "/gateway/roles", 409, "Bu rol zaten var.");
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin/Roles", "/Admin/Roles/Create", new() { ["Name"] = "Admin" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bu rol zaten var.", await HtmlAsync(response));
    }

    [Fact]
    public async Task Role_name_with_invalid_characters_is_rejected_locally()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/roles", 200, Array.Empty<object>());
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin/Roles", "/Admin/Roles/Create", new() { ["Name"] = "ya da boşluk" });

        Assert.Contains("data-valmsg-replace=\"true\">Rol adı yalnız harf ve rakam", await HtmlAsync(response));
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "POST" && r.Path == "/gateway/roles");
    }
}

public class AdminOperationsTests
{
    private static void MapLookups(WebUiTestHost host)
    {
        host.Gateway.MapJson("GET", "/gateway/doctors", 200, new[] { DoctorListItem() });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { Branch() });
    }

    [Fact]
    public async Task Appointments_list_passes_filters_and_shows_names()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapLookups(host);
        var query = MapCapturingQuery(host, "GET", "/gateway/appointments", 200, new[] { Appointment() });
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Appointments?DoctorId=doc1&Status=Confirmed&From=2026-10-01&To=2026-10-31&PatientId=p1"));

        var q = Uri.UnescapeDataString(query()!);
        Assert.Contains("doctorId=doc1", q);
        Assert.Contains("status=Confirmed", q);
        Assert.Contains("from=2026-10-01", q);
        Assert.Contains("to=2026-10-31T23:59:59", q);
        Assert.Contains("patientId=p1", q);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("Kardiyoloji", html);
        Assert.Contains("Onaylandı", html);
        Assert.Contains($"/Admin/Appointments/Details/{AppointmentId}", html);
    }

    [Fact]
    public async Task Appointments_unknown_status_filter_is_not_sent_to_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapLookups(host);
        var query = MapCapturingQuery(host, "GET", "/gateway/appointments", 200, Array.Empty<object>());
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Appointments?Status=Bogus"));

        Assert.True(string.IsNullOrEmpty(query()));
        Assert.Contains("Randevu bulunamadı", html);
    }

    [Fact]
    public async Task Appointments_list_still_renders_when_lookup_services_fail()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, new[] { Appointment() });
        var client = await AdminClientAsync(host); // doctors/branches tanımsız (404)

        var html = await HtmlAsync(await client.GetAsync("/Admin/Appointments"));

        Assert.Contains("doc1", html); // ad çözülemeyince Id gösterilir
    }

    [Fact]
    public async Task Appointment_detail_and_delete_flow()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapLookups(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{AppointmentId}", 200, new
        {
            id = AppointmentId,
            doctorId = DoctorId,
            patientId = "p1",
            branchId = BranchId,
            appointmentDate = new DateTime(2026, 10, 20, 9, 30, 0, DateTimeKind.Utc),
            status = "Pending",
            createdAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            detail = new { id = Guid.NewGuid(), appointmentId = AppointmentId, complaint = "Baş ağrısı", diagnosis = "Migren" },
        });
        host.Gateway.MapStatus("DELETE", $"/gateway/appointments/{AppointmentId}", 204);
        var client = await AdminClientAsync(host);

        var detail = await HtmlAsync(await client.GetAsync($"/Admin/Appointments/Details/{AppointmentId}"));
        Assert.Contains("Baş ağrısı", detail);
        Assert.Contains("Migren", detail);

        Assert.Contains("emin misiniz", await HtmlAsync(await client.GetAsync($"/Admin/Appointments/Delete/{AppointmentId}")));
        Assert.False(Called(host, "DELETE", $"/gateway/appointments/{AppointmentId}"));

        var response = await host.PostFormAsync(client, $"/Admin/Appointments/Delete/{AppointmentId}", []);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(Called(host, "DELETE", $"/gateway/appointments/{AppointmentId}"));
    }

    [Fact]
    public async Task Payments_list_is_paged_with_filters()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapLookups(host);
        var query = MapCapturingQuery(host, "GET", "/gateway/payments", 200, Paged([Payment()], total: 25));
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Payments?Status=Paid&PatientId=p1"));

        var q = Uri.UnescapeDataString(query()!);
        Assert.Contains("status=Paid", q);
        Assert.Contains("patientId=p1", q);
        Assert.Contains("page=1", q);
        Assert.Contains("pageSize=10", q);
        Assert.Contains("450.50 TRY", html);
        Assert.Contains("Ödendi", html);
        Assert.Contains("INV-0001", html);
        Assert.Contains("Sonraki", html);
        Assert.Contains("/Admin/Payments/Refund/", html); // yalnız ödenmişte iade bağlantısı
    }

    [Fact]
    public async Task Payments_empty_state_and_refunded_has_no_refund_link()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapLookups(host);
        host.Gateway.MapJson("GET", "/gateway/payments", 200, Paged([]));
        var client = await AdminClientAsync(host);
        Assert.Contains("Ödeme bulunamadı", await HtmlAsync(await client.GetAsync("/Admin/Payments")));

        host.Gateway.MapJson("GET", $"/gateway/payments/{PaymentId}", 200, Payment("Refunded"));
        var detail = await HtmlAsync(await client.GetAsync($"/Admin/Payments/Details/{PaymentId}"));
        Assert.Contains("İade edildi", detail);
        Assert.DoesNotContain("/Admin/Payments/Refund/", detail);
    }

    [Fact]
    public async Task Refund_get_only_confirms_and_post_calls_refund_endpoint()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapLookups(host);
        host.Gateway.MapJson("GET", $"/gateway/payments/{PaymentId}", 200, Payment());
        host.Gateway.MapJson("POST", $"/gateway/payments/{PaymentId}/refund", 200, Payment("Refunded"));
        var client = await AdminClientAsync(host);

        var confirm = await HtmlAsync(await client.GetAsync($"/Admin/Payments/Refund/{PaymentId}"));
        Assert.Contains("iade etmek istediğinize emin misiniz", confirm);
        Assert.False(Called(host, "POST", $"/gateway/payments/{PaymentId}/refund"));

        var response = await host.PostFormAsync(client, $"/Admin/Payments/Refund/{PaymentId}", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Payments/Details/{PaymentId}", Location(response));
        Assert.Equal("Bearer", Last(host, "POST", $"/gateway/payments/{PaymentId}/refund").Authorization.Split(' ')[0]);
    }

    [Fact]
    public async Task Refund_conflict_message_from_backend_is_shown()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapLookups(host);
        host.Gateway.MapJson("GET", $"/gateway/payments/{PaymentId}", 200, Payment());
        host.MapProblem("POST", $"/gateway/payments/{PaymentId}/refund", 409, "Bu ödeme zaten iade edilmiş.");
        var client = await AdminClientAsync(host);

        var response = await host.PostFormAsync(client, $"/Admin/Payments/Refund/{PaymentId}", []);
        var html = await HtmlAsync(await client.GetAsync(Location(response)!));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Bu ödeme zaten iade edilmiş.", html);
    }

    [Fact]
    public async Task Invoices_list_and_detail()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapLookups(host);
        var query = MapCapturingQuery(host, "GET", "/gateway/invoices", 200, Paged([Invoice()]));
        host.Gateway.MapJson("GET", $"/gateway/invoices/{InvoiceId}", 200, Invoice());
        var client = await AdminClientAsync(host);

        var list = await HtmlAsync(await client.GetAsync("/Admin/Invoices?DoctorId=doc1"));
        Assert.Contains("INV-0001", list);
        Assert.Contains("doctorId=doc1", Uri.UnescapeDataString(query()!));

        var detail = await HtmlAsync(await client.GetAsync($"/Admin/Invoices/Details/{InvoiceId}"));
        Assert.Contains("Muayene ücreti", detail);
        Assert.Contains("450.50 TRY", detail);
        Assert.Contains("KDV (%8)", detail);
    }

    [Fact]
    public async Task Invoices_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/invoices", 200, Paged([]));
        var client = await AdminClientAsync(host);

        Assert.Contains("Fatura bulunamadı", await HtmlAsync(await client.GetAsync("/Admin/Invoices")));
    }

    [Fact]
    public async Task Prescriptions_list_and_detail_are_read_only()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapLookups(host);
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, new[]
        {
            new { id = PrescriptionId, appointmentId = AppointmentId, doctorId = DoctorId, patientId = "p1", issuedAt = new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc), itemCount = 2 },
        });
        host.Gateway.MapJson("GET", $"/gateway/prescriptions/{PrescriptionId}", 200, new
        {
            id = PrescriptionId,
            appointmentId = AppointmentId,
            doctorId = DoctorId,
            patientId = "p1",
            issuedAt = new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc),
            notes = "Bol sıvı",
            items = new[] { new { id = Guid.NewGuid(), lineNo = 1, medicineName = "Parol", dosage = "500 mg", frequency = "Günde 3", durationDays = 5, instructions = "Tok karnına" } },
        });
        var client = await AdminClientAsync(host);

        var list = await HtmlAsync(await client.GetAsync("/Admin/Prescriptions"));
        Assert.Contains("Dr. Ali Veli", list);
        Assert.Contains($"/Admin/Prescriptions/Details/{PrescriptionId}", list);

        var detail = await HtmlAsync(await client.GetAsync($"/Admin/Prescriptions/Details/{PrescriptionId}"));
        Assert.Contains("Parol", detail);
        Assert.Contains("Bol sıvı", detail);
        Assert.DoesNotContain("/Admin/Prescriptions/Edit", detail); // salt okunur: düzenleme/silme bağlantısı yok
        Assert.DoesNotContain("/Admin/Prescriptions/Delete", detail);
    }

    [Fact]
    public async Task Prescriptions_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        var client = await AdminClientAsync(host);

        Assert.Contains("Reçete bulunamadı", await HtmlAsync(await client.GetAsync("/Admin/Prescriptions")));
    }
}
