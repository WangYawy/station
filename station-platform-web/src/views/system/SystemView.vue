<script setup lang="ts">
import { ref } from 'vue'
import DeptTab from './DeptTab.vue'
import UsersTab from './UsersTab.vue'
import RolesTab from './RolesTab.vue'
import AuditTab from './AuditTab.vue'

const active = ref('depts')
</script>

<template>
  <el-tabs v-model="active">
    <el-tab-pane label="组织架构" name="depts"><DeptTab /></el-tab-pane>
    <el-tab-pane label="用户管理" name="users"><UsersTab /></el-tab-pane>
    <el-tab-pane label="角色管理" name="roles"><RolesTab /></el-tab-pane>
    <el-tab-pane label="审计日志" name="audit"><AuditTab /></el-tab-pane>
  </el-tabs>
</template>
