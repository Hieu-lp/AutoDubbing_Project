-- Chỉ đọc metadata và so sánh chuỗi; không chèn/sửa/xóa tài khoản.
-- Chạy sau khi bạn đã áp dụng ConfigureAdminAuthentication.
SET NOCOUNT ON;

IF DB_NAME() <> N'AutoDubbingDb'
    THROW 51100, N'Hay chon database AutoDubbingDb truoc khi xac minh.', 1;

IF NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20261019172708_ConfigureAdminAuthentication')
    THROW 51101, N'Migration ConfigureAdminAuthentication chua duoc ap dung.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns AS c
    WHERE c.object_id = OBJECT_ID(N'dbo.NGUOI_DUNG')
        AND c.name = N'ten_dang_nhap'
        AND TYPE_NAME(c.user_type_id) = N'nvarchar'
        AND c.max_length = 200
        AND c.is_nullable = 0
        AND c.collation_name = N'Latin1_General_100_BIN2')
    THROW 51102, N'Username chua la nvarchar(100) NOT NULL voi collation BIN2.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    INNER JOIN sys.index_columns AS ic
        ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    INNER JOIN sys.columns AS c
        ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID(N'dbo.NGUOI_DUNG')
        AND i.name = N'IX_NGUOI_DUNG_ten_dang_nhap'
        AND i.is_unique = 1 AND i.is_disabled = 0 AND i.has_filter = 0
        AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
        AND c.name = N'ten_dang_nhap' AND ic.key_ordinal = 1
        AND (SELECT COUNT(*) FROM sys.index_columns AS k
            WHERE k.object_id = i.object_id AND k.index_id = i.index_id
                AND k.key_ordinal > 0) = 1)
    THROW 51103, N'Thieu unique index hoat dong tren rieng cot username.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.NGUOI_DUNG')
        AND name = N'CK_NGUOI_DUNG_trang_thai'
        AND is_disabled = 0 AND is_not_trusted = 0)
    THROW 51104, N'Rang buoc trang thai chua duoc bat va xac thuc.', 1;

IF N'Admin' COLLATE Latin1_General_100_BIN2 = N'admin'
    THROW 51105, N'Collation khong phan biet hoa thuong nhu mong doi.', 1;

SELECT c.name, TYPE_NAME(c.user_type_id) AS data_type,
    c.max_length AS max_length_bytes, c.max_length / 2 AS max_length_utf16,
    c.collation_name, c.is_nullable
FROM sys.columns AS c
WHERE c.object_id = OBJECT_ID(N'dbo.NGUOI_DUNG') AND c.name = N'ten_dang_nhap';

SELECT name, is_unique, is_disabled, has_filter
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.NGUOI_DUNG') AND name = N'IX_NGUOI_DUNG_ten_dang_nhap';

SELECT N'PASS: username unique, nvarchar(100), phan biet hoa/thuong; rang buoc trang thai dang hoat dong.' AS verification;
