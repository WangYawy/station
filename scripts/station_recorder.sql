/*
 Navicat Premium Data Transfer

 Source Server         : station
 Source Server Type    : SQLite
 Source Server Version : 3017000
 Source Schema         : main

 Target Server Type    : SQLite
 Target Server Version : 3017000
 File Encoding         : 65001

 Date: 19/10/2026 09:02:00
*/

PRAGMA foreign_keys = false;

-- ----------------------------
-- Table structure for station_recorder
-- ----------------------------
DROP TABLE IF EXISTS "station_recorder";
CREATE TABLE "station_recorder" (
  "Id" bigint NOT NULL,
  "SerialNumber" varchar(64) NOT NULL,
  "Code" varchar(64) NOT NULL,
  "Name" varchar(64) NOT NULL,
  "Model" varchar(64) NOT NULL,
  "Protocol" integer NOT NULL,
  "BoundUserId" bigint,
  "DeptId" bigint,
  "IsAuthorized" bit NOT NULL,
  "IsActive" bit NOT NULL,
  "CreatedAt" datetime NOT NULL,
  "CreatedBy" bigint,
  "UpdatedAt" datetime,
  "UpdatedBy" bigint,
  "IsDeleted" bit NOT NULL,
  "DeletedAt" datetime,
  "DeletedBy" bigint,
  PRIMARY KEY ("Id")
);

-- ----------------------------
-- Records of "station_recorder"
-- ----------------------------
INSERT INTO "station_recorder" VALUES (1, 'dev1', 'dev-code', '记录仪1', 'JW7137A', 0, 202610140000000001, 1, 1, 1, '2026-10-19 9:00', NULL, NULL, NULL, 0, NULL, NULL);

-- ----------------------------
-- Indexes structure for table station_recorder
-- ----------------------------
CREATE UNIQUE INDEX "uk_recorder_serial"
ON "station_recorder" (
  "SerialNumber" ASC
);

PRAGMA foreign_keys = true;
