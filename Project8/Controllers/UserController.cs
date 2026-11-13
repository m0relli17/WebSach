using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Caching;
using System.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Mail;
using System.IO;

using WebBanSach.Models.Data;
using WebBanSach.Areas.Admin.Models;

namespace WebBanSach.Controllers
{
    public class UserController : Controller
    {
        private readonly BSDBContext db = new BSDBContext();
        public static string Phone(string value)
        {
            var s = System.Text.RegularExpressions.Regex.Replace(value ?? "", @"[\s().-]", "");
            if (s.StartsWith("0")) s = "+84" + s.Substring(1);
            return System.Text.RegularExpressions.Regex.IsMatch(s, @"^\+84[35789]\d{8}$") ? s : null;
        }
        internal static string Hash(string password)
        {
            byte[] salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            using (var kdf = new Rfc2898DeriveBytes(password, salt, 210000, HashAlgorithmName.SHA256))
                return "PBKDF2$210000$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(kdf.GetBytes(32));
        }
        internal static bool Matches(string input, string stored)
        {
            if (input == null || stored == null) return false;
            if (!stored.StartsWith("PBKDF2$")) return string.Equals(input, stored, StringComparison.Ordinal);
            try {
                var a = stored.Split('$');
                using (var kdf = new Rfc2898DeriveBytes(input, Convert.FromBase64String(a[2]), int.Parse(a[1]), HashAlgorithmName.SHA256)) {
                    var actual = kdf.GetBytes(32); var expected = Convert.FromBase64String(a[3]);
                    if (expected.Length != actual.Length) return false;
                    int diff = 0; for(int i=0;i<actual.Length;i++) diff |= actual[i] ^ expected[i];
                    return diff == 0;
                }
            } catch { return false; }
        }
        public ActionResult Index() { return RedirectToAction("EditUser"); }
        [HttpGet] public ActionResult Register() { return View(); }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Register(KhachHang model, string ConfirmPassword)
        {
            model.TaiKhoan = (model.TaiKhoan ?? "").Trim();
            model.Email = (model.Email ?? "").Trim().ToLowerInvariant();
            if(!ValidEmail(model.Email)) ModelState.AddModelError("", "Địa chỉ email không hợp lệ.");
            var phone = Phone(model.DienThoai);
            if (!System.Text.RegularExpressions.Regex.IsMatch(model.TaiKhoan, @"^[A-Za-z0-9_.-]{3,50}$"))
                ModelState.AddModelError("", "Tên đăng nhập: 3–50 ký tự, không dấu và không có khoảng trắng.");
            if (model.MatKhau == null || model.MatKhau.Length < 8 || model.MatKhau.Length > 128 || string.IsNullOrWhiteSpace(model.MatKhau))
                ModelState.AddModelError("", "Mật khẩu từ 8–128 ký tự, không được chỉ gồm khoảng trắng.");
            if (!string.Equals(model.MatKhau, ConfirmPassword, StringComparison.Ordinal)) ModelState.AddModelError("", "Hai mật khẩu chưa khớp.");
            if (phone == null) ModelState.AddModelError("", "Nhập số di động Việt Nam hợp lệ (ví dụ 09xxxxxxxx).");
            if (db.KhachHangs.Any(x => x.TaiKhoan == model.TaiKhoan || x.Email == model.Email || x.DienThoai == phone))
                ModelState.AddModelError("", "Tên đăng nhập, email hoặc số điện thoại đã được sử dụng. Hãy đăng nhập để tiếp tục xác minh.");
            if (!ModelState.IsValid) return View(model);
            var customer = new KhachHang { TaiKhoan=model.TaiKhoan, MatKhau=Hash(model.MatKhau),
                TenKH=model.TenKH, Email=model.Email, DiaChi=model.DiaChi, DienThoai=phone,
                NgaySinh=model.NgaySinh, NgayTao=DateTime.Now, TrangThai=true, EmailVerified=false };
            try { db.KhachHangs.Add(customer); db.SaveChanges(); }
            catch (System.Data.Entity.Infrastructure.DbUpdateException) {
                ModelState.AddModelError("", "Không tạo được tài khoản. Kiểm tra thông tin trùng và chạy script nâng cấp database."); return View(model);
            }
            ClearOtp();
            Session["PendingUserId"] = customer.MaKH;
            Session["User"] = null;
            TempData["Message"] = "Tài khoản đã tạo. Bấm gửi mã để xác minh email trước khi mua hàng.";
            return RedirectToAction("VerifyEmail");
        }
        [HttpGet] public ActionResult LoginPage() { return View(); }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult LoginPage(LoginModel model)
        {
            var username = (model.TaiKhoan ?? "").Trim();
            string key = "login:" + Request.UserHostAddress + ":" + username.ToLowerInvariant();
            int attempts = (HttpRuntime.Cache[key] as int?) ?? 0;
            if (attempts >= 10) { ModelState.AddModelError("", "Thử đăng nhập quá nhiều lần. Vui lòng đợi 15 phút."); return View(model); }
            if (!ModelState.IsValid) return View(model);
            var customer = db.KhachHangs.FirstOrDefault(x=>x.TaiKhoan==username);
            if (customer == null || !Matches(model.MatKhau,customer.MatKhau)) {
                HttpRuntime.Cache.Insert(key, attempts+1, null, DateTime.UtcNow.AddMinutes(15), Cache.NoSlidingExpiration);
                ModelState.AddModelError("", "Tài khoản hoặc mật khẩu không đúng."); return View(model);
            }
            if (!customer.TrangThai) { ModelState.AddModelError("", "Tài khoản chưa hoạt động hoặc đã bị khóa. Liên hệ quản trị viên."); return View(model); }
            HttpRuntime.Cache.Remove(key);
            if (!customer.MatKhau.StartsWith("PBKDF2$")) { customer.MatKhau=Hash(model.MatKhau); db.SaveChanges(); }
            if (!customer.EmailVerified) {
                ClearOtp(); Session["User"]=null; Session["PendingUserId"]=customer.MaKH; return RedirectToAction("VerifyEmail");
            }
            Session["User"] = customer.TaiKhoan;
            Session["PendingUserId"] = null;
            return RedirectToAction("Index", "Cart");
        }
        [ChildActionOnly] public ActionResult Login() { return PartialView(); }
        [HttpPost, ValidateAntiForgeryToken] public ActionResult Logout() {
            Session.Clear(); Session.Abandon(); return RedirectToAction("Index","Home");
        }
        private KhachHang Pending() {
            var id=Session["PendingUserId"] as int?;
            return id.HasValue ? db.KhachHangs.Find(id.Value) : null;
        }
        [HttpGet] public ActionResult VerifyEmail() {
            var kh=Pending(); if(kh==null) return RedirectToAction("LoginPage");
            ViewBag.Email = MaskEmail(kh.Email);
            return View();
        }
        private sealed class Counter { public int Value; }
        private static readonly object RateLock = new object();
        private static bool Permit(string key, int max, int minutes) {
            lock(RateLock) {
                var counter=HttpRuntime.Cache[key] as Counter;
                if(counter==null) { counter=new Counter(); HttpRuntime.Cache.Insert(key,counter,null,DateTime.UtcNow.AddMinutes(minutes),Cache.NoSlidingExpiration); }
                if(counter.Value>=max) return false;
                counter.Value++;
                return true;
            }
        }
        private static bool ValidEmail(string value) {
            if(string.IsNullOrWhiteSpace(value) || value.Length>50) return false;
            try { return new MailAddress(value).Address==value && !value.Contains("\r") && !value.Contains("\n"); } catch { return false; }
        }
        private static string MaskEmail(string value) {
            if(!ValidEmail(value)) return "Email chưa hợp lệ";
            int at=value.IndexOf('@'); return value.Substring(0,1)+"***"+value.Substring(at);
        }
        private bool LocalPickup { get { return ConfigurationManager.AppSettings["EmailOtpMode"]=="LocalPickup" && Request.IsLocal; } }
        private void ClearOtp() {
            foreach(var key in new[]{"OtpHash","OtpUntil","OtpAttempts","OtpUserId","OtpEmail"}) Session.Remove(key);
        }
        private void SendEmailCode(string recipient, string code) {
            string mode=ConfigurationManager.AppSettings["EmailOtpMode"];
            if(mode!="Smtp" && !LocalPickup) throw new InvalidOperationException("Chế độ email chưa hợp lệ.");
            string sender=Environment.GetEnvironmentVariable("BOOKSTORE_SMTP_EMAIL") ?? ConfigurationManager.AppSettings["GmailSenderEmail"];
            string password=Environment.GetEnvironmentVariable("BOOKSTORE_SMTP_APP_PASSWORD") ?? ConfigurationManager.AppSettings["GmailAppPassword"];
            if(LocalPickup && string.IsNullOrWhiteSpace(sender)) sender="noreply@bookstore.local";
            if(!LocalPickup && (string.IsNullOrWhiteSpace(sender)||string.IsNullOrWhiteSpace(password)))
                throw new InvalidOperationException("Chưa cấu hình Gmail gửi mã.");
            using(var mail=new MailMessage()) using(var client=new SmtpClient()) {
                mail.From=new MailAddress(sender,"Nhà sách"); mail.To.Add(new MailAddress(recipient));
                mail.Subject="Mã xác minh tài khoản nhà sách";
                mail.SubjectEncoding=Encoding.UTF8; mail.BodyEncoding=Encoding.UTF8;
                mail.Body="Mã xác minh của bạn: "+code+"\r\nMã có hiệu lực 5 phút, chỉ dùng một lần. Không chia sẻ mã cho người khác.\r\nNếu bạn không yêu cầu mã này, hãy bỏ qua email.";
                mail.IsBodyHtml=false; client.UseDefaultCredentials=false; client.Timeout=15000;
                if(LocalPickup) {
                    string folder=Server.MapPath("~/App_Data/OtpEmails"); Directory.CreateDirectory(folder);
                    client.DeliveryMethod=SmtpDeliveryMethod.SpecifiedPickupDirectory;
                    client.PickupDirectoryLocation=folder; client.EnableSsl=false;
                } else {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    client.Host="smtp.gmail.com"; client.Port=587; client.EnableSsl=true;
                    client.DeliveryMethod=SmtpDeliveryMethod.Network;
                    // Chỉ bỏ dấu cách phân nhóm trong mật khẩu ứng dụng Gmail, KHÔNG sửa mật khẩu khách hàng.
                    client.Credentials=new NetworkCredential(sender,password.Replace(" ",""));
                }
                client.Send(mail);
            }
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult SendOtp() {
            var kh=Pending(); if(kh==null) return RedirectToAction("LoginPage");
            if(!kh.TrangThai) return new HttpStatusCodeResult(403);
            if(!ValidEmail(kh.Email)) { TempData["Message"]="Email tài khoản chưa hợp lệ. Liên hệ quản trị viên cập nhật."; return RedirectToAction("VerifyEmail"); }
            var last=Session["OtpLast"] as DateTime?;
            if(last.HasValue && DateTime.UtcNow-last.Value<TimeSpan.FromSeconds(60)) { TempData["Message"]="Vui lòng đợi 60 giây giữa hai lần gửi."; return RedirectToAction("VerifyEmail"); }
            if(!Permit("otp:email:"+kh.Email.Trim().ToLowerInvariant(),5,60)||!Permit("otp:ip:"+Request.UserHostAddress,15,60)) {
                TempData["Message"]="Đã đạt giới hạn gửi mã. Hãy thử lại sau một giờ."; return RedirectToAction("VerifyEmail");
            }
            Session["OtpLast"]=DateTime.UtcNow;
            try {
                byte[] b=new byte[4]; using(var rng=RandomNumberGenerator.Create()) rng.GetBytes(b);
                string code=(100000+(BitConverter.ToUInt32(b,0)%900000)).ToString();
                SendEmailCode(kh.Email,code);
                ClearOtp();
                Session["OtpHash"]=Hash(code); Session["OtpAttempts"]=0;
                Session["OtpUserId"]=kh.MaKH; Session["OtpEmail"]=kh.Email;
                Session["OtpUntil"]=DateTime.UtcNow.AddMinutes(5);
                TempData["Message"]=LocalPickup ? "CHẾ ĐỘ THỬ: thư được lưu trong App_Data/OtpEmails, chưa gửi vào hộp thư thật." : "Đã gửi mã qua email. Kiểm tra Hộp thư đến và Thư rác. Mã có hạn 5 phút.";
            } catch(Exception ex) {
                System.Diagnostics.Trace.TraceError("Email OTP send failed: {0}",ex.GetType().Name);
                TempData["Message"]="Chưa gửi được mã. Kiểm tra GmailSenderEmail, mật khẩu ứng dụng và kết nối mạng; thử lại sau 60 giây.";
            }
            return RedirectToAction("VerifyEmail");
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ConfirmOtp(string code) {
            var kh=Pending(); if(kh==null) return RedirectToAction("LoginPage");
            int tries=(Session["OtpAttempts"] as int?)??0;
            var until=Session["OtpUntil"] as DateTime?;
            if(!until.HasValue || until.Value<DateTime.UtcNow || tries>=5 ||
                (Session["OtpUserId"] as int?)!=kh.MaKH || !string.Equals(Session["OtpEmail"] as string,kh.Email,StringComparison.Ordinal)) {
                ClearOtp(); TempData["Message"]="Mã hết hạn, không thuộc tài khoản này hoặc đã nhập sai 5 lần. Hãy gửi mã mới.";
                return RedirectToAction("VerifyEmail");
            }
            Session["OtpAttempts"]=tries+1;
            code=(code??"").Trim();
            if(!System.Text.RegularExpressions.Regex.IsMatch(code,@"^[0-9]{6}$") || !Matches(code,Session["OtpHash"] as string)) {
                TempData["Message"]="Mã chưa đúng hoặc đã hết hạn."; return RedirectToAction("VerifyEmail");
            }
            if(!kh.TrangThai) return new HttpStatusCodeResult(403);
            kh.EmailVerified=true; db.SaveChanges();
            Session["User"]=kh.TaiKhoan; Session["PendingUserId"]=null; ClearOtp();
            return RedirectToAction("Index","Cart");
        }
        // Không còn kích hoạt bằng mã khách hàng công khai trên URL.
        public ActionResult XacNhan(int khMaKh) { return RedirectToAction("LoginPage"); }
        public ActionResult XacNhanEmail(int khMaKh) { return new HttpStatusCodeResult(410); }
        public ActionResult ThongBaoKichHoat() { return RedirectToAction("LoginPage"); }
        public ActionResult KiemTraThongBaoKichHoat() { return RedirectToAction("VerifyEmail"); }
        [HttpGet] public ActionResult EditUser() {
            string user=Session["User"] as string;
            var kh=db.KhachHangs.FirstOrDefault(x=>x.TaiKhoan==user);
            return kh==null ? (ActionResult)RedirectToAction("LoginPage") : View(kh);
        }
        [HttpPost, ValidateAntiForgeryToken] public ActionResult EditUser(KhachHang model) {
            string user=Session["User"] as string;
            var kh=db.KhachHangs.FirstOrDefault(x=>x.TaiKhoan==user);
            if(kh==null) return RedirectToAction("LoginPage");
            // Không nhận MaKH, trạng thái, mật khẩu hoặc số điện thoại từ form cập nhật hồ sơ.
            if(string.IsNullOrWhiteSpace(model.TenKH)||model.TenKH.Length>50||string.IsNullOrWhiteSpace(model.DiaChi)||model.DiaChi.Length>250) {
                ModelState.Clear(); ModelState.AddModelError("","Họ tên và địa chỉ hợp lệ là bắt buộc."); return View(kh);
            }
            kh.TenKH=model.TenKH.Trim(); kh.DiaChi=model.DiaChi.Trim(); kh.NgaySinh=model.NgaySinh;
            db.SaveChanges(); return RedirectToAction("EditUser");
        }
        protected override void Dispose(bool disposing) { if(disposing) db.Dispose(); base.Dispose(disposing); }
    }
}
