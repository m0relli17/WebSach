# WebBanSach — xác minh bằng mã EMAIL, đặt hàng và theo dõi đơn

Bản này thay hoàn toàn OTP điện thoại bằng mã gửi qua email. Không cần Twilio hay đăng ký dịch vụ SMS. Các tính năng giỏ hàng, COD, theo dõi/hủy đơn và quản trị đơn được giữ lại.

## 1. Cài bản cập nhật

1. Giữ lại code cũ, backup database BSDB2. Giải nén ZIP vào thư mục mới, không chép lẫn file từ bản điện thoại.
2. Mở SSMS → kết nối .\SQLEXPRESS. Nếu đã có BSDB2, chỉ chạy 01_NangCapDatabase.sql. Script dùng được cả khi bạn đã chạy bản SMS trước đó: thêm cột EmailVerified mới; cột PhoneVerified cũ nếu có được giữ lại nhưng code không còn sử dụng. Không chuyển trạng thái xác minh điện thoại thành xác minh email.
3. Chỉ khi chưa có BSDB2, chạy 00_TaoDatabaseMoi.sql trước, sau đó chạy 01_NangCapDatabase.sql.
4. Mở WebBanSach.sln bằng Visual Studio với ASP.NET and web development, .NET Framework 4.8 targeting pack. Restore NuGet Packages cần có internet.
5. Chuỗi kết nối Web.config đã đặt server .\SQLEXPRESS, database BSDB2, Windows Authentication. Nếu khác SSMS của bạn thì sửa tên server.
6. Cấu hình Gmail theo phần 2 rồi Rebuild Solution → Ctrl+F5.
7. Nếu cần: dự án WebBanSach → Set as Startup Project; Properties → Web → IIS Express, Specific Page = Home/Index.

## 2. Cấu hình Gmail gửi mã thật — chỉ chủ web làm một lần

Không cần đăng ký nhà cung cấp SMS. Dùng Gmail của bạn hoặc Gmail riêng của website làm tài khoản gửi; khách có thể nhận mã bằng email của họ.

1. Đăng nhập Gmail dùng để gửi mã.
2. Bật Xác minh 2 bước trong https://myaccount.google.com/security.
3. Mở https://myaccount.google.com/apppasswords, tạo mật khẩu ứng dụng cho "WebBanSach". Chỉ tài khoản đủ điều kiện mới có mục này; tài khoản tổ chức hoặc một số cấu hình bảo mật có thể hạn chế. Nếu không thấy, đọc https://support.google.com/accounts/answer/185833.
4. Trong Visual Studio, mở Web.config ở gốc dự án. Tìm phần appSettings và điền 2 giá trị trống đã có sẵn (không thêm trùng key):

```xml
<add key="EmailOtpMode" value="Smtp" />
<add key="GmailSenderEmail" value="GMAIL_CUA_BAN@gmail.com" />
<add key="GmailAppPassword" value="MAT_KHAU_UNG_DUNG" />
```

Dùng mật khẩu ứng dụng, KHÔNG dùng mật khẩu đăng nhập Gmail. Không gửi mật khẩu vào chat/ảnh và không đẩy file chứa mật khẩu lên GitHub. Nếu giá trị có &, < hoặc dấu nháy, cần XML-escape tương ứng; mật khẩu ứng dụng Gmail thông thường không cần xử lý này.

5. Ctrl+S, khởi động lại web. Đăng ký tài khoản mới bằng email mà bạn có thể mở hộp thư, bấm Gửi mã trên trang xác minh.
6. Mở Hộp thư đến hoặc Thư rác / Spam, lấy mã 6 số → nhập vào website → xác minh → mua hàng.

Cấu hình dùng smtp.gmail.com, cổng 587, STARTTLS. Tham khảo chính thức: https://developers.google.com/workspace/gmail/imap/imap-smtp?hl=vi. Gmail có hạn mức và điều kiện gửi; thư có thể chậm hoặc vào Spam. Bản code không bảo đảm mọi thư đều được nhà cung cấp giao thành công.

Nếu không muốn lưu secret trong file, tạo biến môi trường Windows BOOKSTORE_SMTP_EMAIL và BOOKSTORE_SMTP_APP_PASSWORD. Biến môi trường được ưu tiên hơn appSettings. Sau khi đặt biến, đóng Visual Studio/IIS Express rồi mở lại. Nếu có biến cũ sai thì sửa/xóa biến trước khi thử.

## 3. Thử luồng khi chưa cấu hình Gmail

Trong Web.config đổi EmailOtpMode từ Smtp sang LocalPickup. Chế độ này CHỈ hoạt động khi truy cập localhost:

- Bấm Gửi mã sẽ lưu thư .eml trong Project8/App_Data/OtpEmails.
- Không có email gửi vào hộp thư thật, website hiển thị rõ đang ở chế độ thử.
- Mở file .eml mới nhất bằng Outlook/ứng dụng đọc email. Nếu xem bằng Notepad mà nội dung bị mã hóa Base64, dùng ứng dụng đọc .eml; không nhập chuỗi Base64 làm mã.
- Khi muốn gửi thật, đổi lại Smtp và cấu hình Gmail như phần 2.
- Không có mã cố định hay hiển thị OTP thật trên giao diện; mã được tạo ngẫu nhiên và phía server chỉ giữ bản băm trong phiên.

## 4. Quy tắc xác minh và tài khoản cũ

- Đăng ký → trang xác minh email → bấm Gửi mã → nhập mã đúng → vào giỏ hàng. Tài khoản chưa xác minh không mua được.
- Mã 6 số hết hạn sau 5 phút, tối đa 5 lần nhập sai. Gửi lại sau ít nhất 60 giây; tối đa 5 lượt/email/giờ và 15 lượt/IP/giờ trong một tiến trình.
- Gửi lại thành công làm mã cũ mất hiệu lực. Mã gắn với tài khoản, email và phiên trình duyệt, không dùng chéo tài khoản hoặc dùng lại sau xác minh.
- Sau khi tắt/mở web hoặc mất phiên, đăng nhập lại rồi gửi mã mới.
- Tài khoản cũ PhoneVerified=1 vẫn cần xác minh EMAIL lần đầu. Không tự coi xác minh điện thoại là đã xác minh email.
- TrangThai là trạng thái tài khoản hoạt động/bị khóa, EmailVerified là xác minh email; không nhập nhằng hai ý nghĩa.
- Nếu tài khoản thử My đang TrangThai=0 do bản template cũ, quản trị viên có thể mở riêng tài khoản đó bằng:
  UPDATE dbo.KhachHang SET TrangThai=1 WHERE TaiKhoan=N'My';
- Email tài khoản cũ phải đúng và bạn truy cập được hộp thư. Nếu nhập sai, quản trị viên cần cập nhật email trước. Chưa có luồng tự đổi email/đổi số/quên mật khẩu.
- Số điện thoại vẫn là thông tin liên hệ/giao hàng khi đăng ký, không gửi SMS.

## 5. Đăng nhập và mật khẩu

- Mật khẩu từ 8–128 ký tự khi đăng ký; nhập lại để kiểm tra, có nút hiện/ẩn.
- Giữ nguyên khoảng trắng, chữ hoa/thường, ký tự bên trong mật khẩu. Không tự Trim mật khẩu. Mật khẩu chỉ toàn khoảng trắng bị từ chối. Tên đăng nhập được bỏ khoảng trắng thừa đầu/cuối.
- PBKDF2 SHA-256, salt ngẫu nhiên, 210.000 vòng; cột MatKhau mở rộng 256 để không cắt chuỗi băm. Tài khoản cũ chuyển từ mật khẩu thường sang băm khi đăng nhập đúng.
- Phiên người dùng tách riêng, không còn static khách hàng dùng chung.

## 6. Đặt và theo dõi đơn

- Khách được xem sách/giỏ hàng trước, nhưng phải đăng ký, đăng nhập và xác minh email trước khi đặt.
- COD là phương thức đang hoạt động. Chưa tích hợp thanh toán online, MoMo thật, phí vận chuyển, mã giảm giá, hoàn tiền.
- Checkout lấy giá và tồn kho từ database, không tin giá hoặc mã khách hàng từ trình duyệt. Tạo đơn, chi tiết và trừ tồn trong cùng giao dịch. Nếu thất bại, giữ giỏ để thử lại.
- Đơn hàng của tôi chỉ hiển thị đơn của chính tài khoản; chi tiết đơn và API danh sách cũng kiểm tra chủ sở hữu.
- Khách hủy được đơn Chờ xử lý. Tồn kho hoàn lại đúng một lần.
- Quản trị: /Admin/Login → /Admin/Home/Order. Tài khoản admin mẫu của SQL là admin / admin; cần thay khi sử dụng dữ liệu thật.
- Admin chuyển Chờ xử lý → Đã đóng gói → Đang giao → Đã giao; có thể hủy khi Chờ xử lý hoặc Đã đóng gói. Xác nhận Đã giao / đã thu COD đánh dấu đã thanh toán. Chưa kết nối hãng vận chuyển.
- Trạng thái đơn cũ không được suy đoán tự động; admin kiểm tra thực tế trước khi cập nhật.

## 7. Phạm vi kiểm tra

Đã kiểm tra cú pháp toàn bộ 35 file C#, XML và manifest project, loại bỏ tham chiếu xác minh điện thoại khỏi luồng đang dùng, kiểm tra mã nguồn về ràng buộc OTP/tài khoản và đóng gói ZIP.

Chưa Build bằng MSBuild/Razor trên Windows, chưa chạy script với SQL Server tại môi trường sửa này; chưa gửi thử email thật vì chưa có Gmail/mật khẩu ứng dụng của bạn. Vì vậy cần kiểm tra trên máy bạn trước khi dùng:

1. Tạo tài khoản → gửi mã → email nhận đúng mã; không thấy mã hiển thị trên trang.
2. Sai mã, quá 5 lần, hết 5 phút phải bị từ chối; mã cũ không dùng được sau khi gửi lại.
3. Hai tài khoản trong cùng trình duyệt không dùng chéo mã; tài khoản chưa xác minh không đặt được hàng.
4. Đăng nhập mật khẩu có khoảng trắng nguyên văn thành công; bỏ một khoảng trắng có ý nghĩa phải thất bại.
5. Hai trình duyệt không xem được đơn của nhau kể cả sửa id trên URL.
6. Đặt quá tồn kho hoặc gửi số lượng âm không tạo đơn nửa chừng; giỏ còn khi đặt thất bại.
7. Hủy/cập nhật trạng thái hợp lệ, tồn kho hoàn đúng một lần và khách thấy cập nhật khi tải lại.

Giới hạn: throttle hiện lưu trong bộ nhớ một tiến trình, chưa phải mô hình nhiều máy chủ; các chức năng quản trị cũ ngoài phạm vi sửa chưa được rà soát toàn bộ. Khi triển khai thật cần HTTPS và cookie requireSSL=true. Không chạy script tạo mới lên database đã tồn tại.
