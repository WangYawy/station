<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { ApiError } from '../api/client'

const auth = useAuthStore()
const router = useRouter()
const form = reactive({ userName: '', password: '' })
const loading = ref(false)

async function onSubmit() {
  if (!form.userName || !form.password) {
    ElMessage.warning('请输入账号和密码')
    return
  }
  loading.value = true
  try {
    await auth.login(form.userName, form.password)
    router.push('/stats')
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '登录失败')
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-wrap">
    <el-card class="login-card">
      <h2 class="title">监控管理平台</h2>
      <el-form label-position="top" @submit.prevent="onSubmit">
        <el-form-item label="账号">
          <el-input v-model="form.userName" placeholder="请输入账号" autofocus />
        </el-form-item>
        <el-form-item label="密码">
          <el-input v-model="form.password" type="password" placeholder="请输入密码" show-password @keyup.enter="onSubmit" />
        </el-form-item>
        <el-button type="primary" class="submit" :loading="loading" @click="onSubmit">登 录</el-button>
      </el-form>
    </el-card>
  </div>
</template>

<style scoped>
.login-wrap {
  height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #0a2f6c 0%, #12408a 100%);
}
.login-card {
  width: 360px;
}
.title {
  text-align: center;
  color: #0a2f6c;
  margin: 4px 0 18px;
}
.submit {
  width: 100%;
}
</style>
