---
status: partial
phase: 09-ui-language-switch
source: [09-VERIFICATION.md]
started: 2026-05-01T00:00:00Z
updated: 2026-05-01T00:00:00Z
---

# Phase 9 — Human UAT

## Current Test

[waiting for human testing in Dalamud runtime]

## Tests

### 1. 语言选择器 ComboBox 可见性
expected: 配置窗口标题栏区域显示语言选择下拉框，包含 "中文" 和 "English" 两个选项
result: [pending]

### 2. 切换语言后所有文本即时更新
expected: 从下拉框选择 "English" 后，所有标签、按钮、状态行、表格表头即时切换为英文，无需关闭或重新打开窗口
result: [pending]

### 3. 语言偏好持久化
expected: 选择 "English" 后重载插件（/xlplugins 关闭再打开），重新打开配置窗口时语言仍为 English
result: [pending]

## Summary

total: 3
passed: 0
issues: 0
pending: 3
skipped: 0
blocked: 0

## Gaps
