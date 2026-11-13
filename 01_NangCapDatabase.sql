USE [BSDB2];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.KhachHang','EmailVerified') IS NULL
    ALTER TABLE dbo.KhachHang ADD EmailVerified BIT NOT NULL CONSTRAINT DF_KhachHang_EmailVerified DEFAULT(0);
ALTER TABLE dbo.KhachHang ALTER COLUMN MatKhau VARCHAR(256) NOT NULL;
IF COL_LENGTH('dbo.DonDatHang','ThanhToan') IS NULL
    ALTER TABLE dbo.DonDatHang ADD ThanhToan INT NULL;
IF COL_LENGTH('dbo.DonDatHang','Tracking') IS NULL
    ALTER TABLE dbo.DonDatHang ADD Tracking INT NULL;
IF EXISTS(SELECT TaiKhoan FROM dbo.KhachHang GROUP BY TaiKhoan HAVING COUNT(*)>1)
    THROW 50001, 'Duplicate usernames: review before upgrading.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_KhachHang_TaiKhoan' AND object_id=OBJECT_ID('dbo.KhachHang'))
    CREATE UNIQUE INDEX UX_KhachHang_TaiKhoan ON dbo.KhachHang(TaiKhoan);
COMMIT;
GO
-- Don cu: khong suy doan tinh trang giao hang/thanh toan. Admin kiem tra thuc te truoc khi cap nhat.
SELECT MaKH,TaiKhoan,DienThoai,TrangThai,EmailVerified FROM dbo.KhachHang;
