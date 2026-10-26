USE [AutoDubbingDb];
GO
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE TABLE [NGUOI_DUNG] (
        [id] int NOT NULL IDENTITY,
        [ten_dang_nhap] nvarchar(max) NOT NULL,
        [mat_khau_hash] nvarchar(max) NOT NULL,
        [ho_ten] nvarchar(max) NOT NULL,
        [trang_thai] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_NGUOI_DUNG] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE TABLE [NHAT_KY_HE_THONG] (
        [id] int NOT NULL IDENTITY,
        [nguoi_dung_id] int NULL,
        [thanh_phan] nvarchar(max) NOT NULL,
        [muc_do] nvarchar(max) NOT NULL,
        [thong_diep] nvarchar(max) NOT NULL,
        [thoi_diem] datetime2 NOT NULL,
        CONSTRAINT [PK_NHAT_KY_HE_THONG] PRIMARY KEY ([id]),
        CONSTRAINT [FK_NHAT_KY_HE_THONG_NGUOI_DUNG_nguoi_dung_id] FOREIGN KEY ([nguoi_dung_id]) REFERENCES [NGUOI_DUNG] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE TABLE [POI] (
        [id] int NOT NULL IDENTITY,
        [nguoi_dung_id] int NOT NULL,
        [ten] nvarchar(200) NOT NULL,
        [lat] float NOT NULL,
        [lng] float NOT NULL,
        [ban_kinh_kich_hoat] float NOT NULL,
        [anh_thumbnail] nvarchar(max) NULL,
        [mo_ta_goc] nvarchar(max) NOT NULL,
        [ngon_ngu_goc] nvarchar(max) NOT NULL,
        [trang_thai] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_POI] PRIMARY KEY ([id]),
        CONSTRAINT [FK_POI_NGUOI_DUNG_nguoi_dung_id] FOREIGN KEY ([nguoi_dung_id]) REFERENCES [NGUOI_DUNG] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE TABLE [SESSION] (
        [id] int NOT NULL IDENTITY,
        [nguoi_dung_id] int NOT NULL,
        [ten] nvarchar(max) NOT NULL,
        [mo_ta] nvarchar(max) NOT NULL,
        [trang_thai] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_SESSION] PRIMARY KEY ([id]),
        CONSTRAINT [FK_SESSION_NGUOI_DUNG_nguoi_dung_id] FOREIGN KEY ([nguoi_dung_id]) REFERENCES [NGUOI_DUNG] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE TABLE [AUDIO] (
        [id] int NOT NULL IDENTITY,
        [poi_id] int NOT NULL,
        [ngon_ngu] nvarchar(max) NOT NULL,
        [duong_dan_file] nvarchar(max) NOT NULL,
        [thoi_luong] int NOT NULL,
        [trang_thai] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_AUDIO] PRIMARY KEY ([id]),
        CONSTRAINT [FK_AUDIO_POI_poi_id] FOREIGN KEY ([poi_id]) REFERENCES [POI] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE TABLE [BAN_DICH] (
        [id] int NOT NULL IDENTITY,
        [poi_id] int NOT NULL,
        [ngon_ngu] nvarchar(max) NOT NULL,
        [van_ban] nvarchar(max) NOT NULL,
        [trang_thai] nvarchar(max) NOT NULL,
        [thoi_diem_cap_nhat] datetime2 NOT NULL,
        CONSTRAINT [PK_BAN_DICH] PRIMARY KEY ([id]),
        CONSTRAINT [FK_BAN_DICH_POI_poi_id] FOREIGN KEY ([poi_id]) REFERENCES [POI] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE TABLE [SU_KIEN_SU_DUNG] (
        [id] int NOT NULL IDENTITY,
        [poi_id] int NOT NULL,
        [loai_su_kien] nvarchar(max) NOT NULL,
        [ngon_ngu] nvarchar(max) NOT NULL,
        [thoi_diem] datetime2 NOT NULL,
        CONSTRAINT [PK_SU_KIEN_SU_DUNG] PRIMARY KEY ([id]),
        CONSTRAINT [FK_SU_KIEN_SU_DUNG_POI_poi_id] FOREIGN KEY ([poi_id]) REFERENCES [POI] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE TABLE [LO_TRINH] (
        [id] int NOT NULL IDENTITY,
        [session_id] int NOT NULL,
        [ten] nvarchar(max) NOT NULL,
        [danh_sach_poi] nvarchar(max) NOT NULL,
        [trang_thai] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_LO_TRINH] PRIMARY KEY ([id]),
        CONSTRAINT [FK_LO_TRINH_SESSION_session_id] FOREIGN KEY ([session_id]) REFERENCES [SESSION] ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AUDIO_poi_id] ON [AUDIO] ([poi_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BAN_DICH_poi_id] ON [BAN_DICH] ([poi_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LO_TRINH_session_id] ON [LO_TRINH] ([session_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_NHAT_KY_HE_THONG_nguoi_dung_id] ON [NHAT_KY_HE_THONG] ([nguoi_dung_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_POI_nguoi_dung_id] ON [POI] ([nguoi_dung_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SESSION_nguoi_dung_id] ON [SESSION] ([nguoi_dung_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SU_KIEN_SU_DUNG_poi_id] ON [SU_KIEN_SU_DUNG] ([poi_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019154054_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019154054_InitialCreate', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019172708_ConfigureAdminAuthentication'
)
BEGIN
    IF EXISTS (SELECT 1 FROM [NGUOI_DUNG] WHERE DATALENGTH([ten_dang_nhap]) > 200)
    BEGIN
        THROW 51000, N'Username vuot qua 100 ky tu. Can xu ly du lieu truoc khi migrate.', 1;
    END;

    IF EXISTS (
        SELECT [ten_dang_nhap] COLLATE Latin1_General_100_BIN2
        FROM [NGUOI_DUNG]
        GROUP BY [ten_dang_nhap] COLLATE Latin1_General_100_BIN2
        HAVING COUNT(*) > 1)
    BEGIN
        THROW 51001, N'Username bi trung theo collation phan biet hoa thuong. Can xu ly du lieu truoc khi migrate.', 1;
    END;

    IF EXISTS (
        SELECT 1 FROM [NGUOI_DUNG]
        WHERE DATALENGTH([trang_thai]) <> 12
            OR [trang_thai] COLLATE Latin1_General_100_BIN2 NOT IN (N'Active', N'Locked'))
    BEGIN
        THROW 51002, N'Trang thai tai khoan phai la Active hoac Locked. Can xu ly du lieu truoc khi migrate.', 1;
    END;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019172708_ConfigureAdminAuthentication'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[NGUOI_DUNG]') AND [c].[name] = N'trang_thai');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [NGUOI_DUNG] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [NGUOI_DUNG] ALTER COLUMN [trang_thai] nvarchar(6) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019172708_ConfigureAdminAuthentication'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[NGUOI_DUNG]') AND [c].[name] = N'ten_dang_nhap');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [NGUOI_DUNG] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [NGUOI_DUNG] ALTER COLUMN [ten_dang_nhap] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019172708_ConfigureAdminAuthentication'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NGUOI_DUNG_ten_dang_nhap] ON [NGUOI_DUNG] ([ten_dang_nhap]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019172708_ConfigureAdminAuthentication'
)
BEGIN
    EXEC(N'ALTER TABLE [NGUOI_DUNG] ADD CONSTRAINT [CK_NGUOI_DUNG_trang_thai] CHECK (DATALENGTH([trang_thai]) = 12 AND [trang_thai] COLLATE Latin1_General_100_BIN2 IN (''Active'', ''Locked''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019172708_ConfigureAdminAuthentication'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019172708_ConfigureAdminAuthentication', N'10.0.13');
END;

COMMIT;
GO

