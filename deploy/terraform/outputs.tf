output "api_url"        { value = module.ecs.alb_dns_name }
output "rds_endpoint"   { value = module.rds.endpoint }
output "redis_endpoint" { value = module.elasticache.endpoint }
output "s3_bucket"      { value = module.s3.bucket_name }
output "vpc_id"         { value = module.vpc.vpc_id }
